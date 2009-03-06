﻿//-----------------------------------------------------------------------
// <copyright file="CoordinatingChannel.cs" company="Andrew Arnott">
//     Copyright (c) Andrew Arnott. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace DotNetOpenAuth.Test.Mocks {
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Text;
	using System.Threading;
	using DotNetOpenAuth.Messaging;
	using DotNetOpenAuth.Messaging.Reflection;
	using DotNetOpenAuth.Test.OpenId;
	using Microsoft.VisualStudio.TestTools.UnitTesting;

	internal class CoordinatingChannel : Channel {
		/// <summary>
		/// A lock to use when checking and setting the <see cref="waitingForMessage"/> 
		/// or the <see cref="simulationCompleted"/> fields.
		/// </summary>
		/// <remarks>
		/// This is a static member so that all coordinating channels share a lock
		/// since they peak at each others fields.
		/// </remarks>
		private static readonly object waitingForMessageCoordinationLock = new object();

		/// <summary>
		/// The original product channel whose behavior is being modified to work
		/// better in automated testing.
		/// </summary>
		private Channel wrappedChannel;

		/// <summary>
		/// A flag set to true when this party in a two-party test has completed
		/// its part of the testing.
		/// </summary>
		private bool simulationCompleted;

		/// <summary>
		/// A thread-coordinating signal that is set when another thread has a 
		/// message ready for this channel to receive.
		/// </summary>
		private EventWaitHandle incomingMessageSignal = new AutoResetEvent(false);

		/// <summary>
		/// A flag used to indicate when this channel is waiting for a message
		/// to arrive.
		/// </summary>
		private bool waitingForMessage;

		/// <summary>
		/// An incoming message that has been posted by a remote channel and 
		/// is waiting for receipt by this channel.
		/// </summary>
		private IDictionary<string, string> incomingMessage;

		private MessageReceivingEndpoint incomingMessageRecipient;

		/// <summary>
		/// A delegate that gets a chance to peak at and fiddle with all 
		/// incoming messages.
		/// </summary>
		private Action<IProtocolMessage> incomingMessageFilter;

		/// <summary>
		/// A delegate that gets a chance to peak at and fiddle with all 
		/// outgoing messages.
		/// </summary>
		private Action<IProtocolMessage> outgoingMessageFilter;

		/// <summary>
		/// Initializes a new instance of the <see cref="CoordinatingChannel"/> class.
		/// </summary>
		/// <param name="wrappedChannel">The wrapped channel.  Must not be null.</param>
		/// <param name="incomingMessageFilter">The incoming message filter.  May be null.</param>
		/// <param name="outgoingMessageFilter">The outgoing message filter.  May be null.</param>
		internal CoordinatingChannel(Channel wrappedChannel, Action<IProtocolMessage> incomingMessageFilter, Action<IProtocolMessage> outgoingMessageFilter)
			: base(GetMessageFactory(wrappedChannel), wrappedChannel.BindingElements.ToArray()) {
			ErrorUtilities.VerifyArgumentNotNull(wrappedChannel, "wrappedChannel");

			this.wrappedChannel = wrappedChannel;
			this.incomingMessageFilter = incomingMessageFilter;
			this.outgoingMessageFilter = outgoingMessageFilter;

			// Preserve any customized binding element ordering.
			this.CustomizeBindingElementOrder(this.wrappedChannel.OutgoingBindingElements, this.wrappedChannel.IncomingBindingElements);
		}

		/// <summary>
		/// Gets or sets the coordinating channel used by the other party.
		/// </summary>
		internal CoordinatingChannel RemoteChannel { get; set; }

		/// <summary>
		/// Indicates that the simulation that uses this channel has completed work.
		/// </summary>
		/// <remarks>
		/// Calling this method is not strictly necessary, but it gives the channel
		/// coordination a chance to recognize when another channel is left dangling
		/// waiting for a message from another channel that may never come.
		/// </remarks>
		internal void Close() {
			lock (waitingForMessageCoordinationLock) {
				this.simulationCompleted = true;
				if (this.RemoteChannel.waitingForMessage && this.RemoteChannel.incomingMessage == null) {
					TestUtilities.TestLogger.Debug("CoordinatingChannel is closing while remote channel is waiting for an incoming message.  Signaling channel to unblock it to receive a null message.");
					this.RemoteChannel.incomingMessageSignal.Set();
				}

				this.Dispose();
			}
		}

		/// <summary>
		/// Replays the specified message as if it were received again.
		/// </summary>
		/// <param name="message">The message to replay.</param>
		internal void Replay(IProtocolMessage message) {
			this.VerifyMessageAfterReceiving(CloneSerializedParts(message));
		}

		/// <summary>
		/// Called from a remote party's thread to post a message to this channel for processing.
		/// </summary>
		/// <param name="message">The message that this channel should receive.  This message will be cloned.</param>
		internal void PostMessage(IProtocolMessage message) {
			ErrorUtilities.VerifyInternal(this.incomingMessage == null, "Oops, a message is already waiting for the remote party!");
			this.incomingMessage = new Dictionary<string, string>(new MessageDictionary(message));
			var directedMessage = message as IDirectedProtocolMessage;
			this.incomingMessageRecipient = directedMessage != null ? new MessageReceivingEndpoint(directedMessage.Recipient, directedMessage.HttpMethods) : null;
			this.incomingMessageSignal.Set();
		}

		protected internal override HttpRequestInfo GetRequestFromContext() {
			MessageReceivingEndpoint recipient;
			var messageData = this.AwaitIncomingMessage(out recipient);
			IDirectedProtocolMessage message = null;
			if (messageData != null) {
				message = this.MessageFactory.GetNewRequestMessage(recipient, messageData);
				if (message != null) {
					MessageSerializer.Get(message.GetType()).Deserialize(messageData, message);
				}
				return new HttpRequestInfo(message, recipient.AllowedMethods);
			} else {
				return new HttpRequestInfo(null, HttpDeliveryMethods.GetRequest);
			}
		}

		protected override IProtocolMessage RequestInternal(IDirectedProtocolMessage request) {
			this.ProcessMessageFilter(request, true);

			// Drop the outgoing message in the other channel's in-slot and let them know it's there.
			this.RemoteChannel.PostMessage(request);

			// Now wait for a response...
			MessageReceivingEndpoint recipient;
			IDictionary<string, string> responseData = this.AwaitIncomingMessage(out recipient);
			ErrorUtilities.VerifyInternal(recipient == null, "The recipient is expected to be null for direct responses.");

			// And deserialize it.
			IDirectResponseProtocolMessage responseMessage = this.MessageFactory.GetNewResponseMessage(request, responseData);
			if (responseMessage == null) {
				return null;
			}

			var responseSerializer = MessageSerializer.Get(responseMessage.GetType());
			responseSerializer.Deserialize(responseData, responseMessage);

			this.ProcessMessageFilter(responseMessage, false);
			return responseMessage;
		}

		protected override UserAgentResponse SendDirectMessageResponse(IProtocolMessage response) {
			this.ProcessMessageFilter(response, true);
			return new CoordinatingUserAgentResponse(response, this.RemoteChannel);
		}

		protected override UserAgentResponse SendIndirectMessage(IDirectedProtocolMessage message) {
			this.ProcessMessageFilter(message, true);
			// In this mock transport, direct and indirect messages are the same.
			return this.SendDirectMessageResponse(message);
		}

		protected override IDirectedProtocolMessage ReadFromRequestInternal(HttpRequestInfo request) {
			if (request.Message != null) {
				this.ProcessMessageFilter(request.Message, false);
			}

			return request.Message;
		}

		protected override IDictionary<string, string> ReadFromResponseInternal(DirectWebResponse response) {
			Channel_Accessor accessor = Channel_Accessor.AttachShadow(this.wrappedChannel);
			return accessor.ReadFromResponseInternal(response);
		}

		protected override void VerifyMessageAfterReceiving(IProtocolMessage message) {
			Channel_Accessor accessor = Channel_Accessor.AttachShadow(this.wrappedChannel);
			accessor.VerifyMessageAfterReceiving(message);
		}

		/// <summary>
		/// Clones a message, instantiating the new instance using <i>this</i> channel's
		/// message factory.
		/// </summary>
		/// <typeparam name="T">The type of message to clone.</typeparam>
		/// <param name="message">The message to clone.</param>
		/// <returns>The new instance of the message.</returns>
		/// <remarks>
		/// This Clone method should <i>not</i> be used to send message clones to the remote
		/// channel since their message factory is not used.
		/// </remarks>
		protected virtual T CloneSerializedParts<T>(T message) where T : class, IProtocolMessage {
			ErrorUtilities.VerifyArgumentNotNull(message, "message");

			IProtocolMessage clonedMessage;
			MessageSerializer serializer = MessageSerializer.Get(message.GetType());
			var fields = serializer.Serialize(message);

			MessageReceivingEndpoint recipient = null;
			var directedMessage = message as IDirectedProtocolMessage;
			var directResponse = message as IDirectResponseProtocolMessage;
			if (directedMessage != null && directedMessage.IsRequest()) {
				if (directedMessage.Recipient != null) {
					recipient = new MessageReceivingEndpoint(directedMessage.Recipient, directedMessage.HttpMethods);
				}

				clonedMessage = this.MessageFactory.GetNewRequestMessage(recipient, fields);
			} else if (directResponse != null && directResponse.IsDirectResponse()) {
				clonedMessage = this.MessageFactory.GetNewResponseMessage(directResponse.OriginatingRequest, fields);
			} else {
				throw new InvalidOperationException("Totally expected a message to implement one of the two derived interface types.");
			}

			ErrorUtilities.VerifyInternal(clonedMessage != null, "Message factory did not generate a message instance for " + message.GetType().Name);

			// Fill the cloned message with data.
			serializer.Deserialize(fields, clonedMessage);

			return (T)clonedMessage;
		}

		private static IMessageFactory GetMessageFactory(Channel channel) {
			ErrorUtilities.VerifyArgumentNotNull(channel, "channel");

			Channel_Accessor accessor = Channel_Accessor.AttachShadow(channel);
			return accessor.MessageFactory;
		}

		private IDictionary<string, string> AwaitIncomingMessage(out MessageReceivingEndpoint recipient) {
			// Special care should be taken so that we don't indefinitely 
			// wait for a message that may never come due to a bug in the product
			// or the test.
			// There are two scenarios that we need to watch out for:
			//  1. Two channels are waiting to receive messages from each other.
			//  2. One channel is waiting for a message that will never come because
			//     the remote party has already finished executing.
			lock (waitingForMessageCoordinationLock) {
				// It's possible that a message was just barely transmitted either to this
				// or the remote channel.  So it's ok for the remote channel to be waiting
				// if either it or we are already about to receive a message.
				ErrorUtilities.VerifyInternal(!this.RemoteChannel.waitingForMessage || this.RemoteChannel.incomingMessage != null || this.incomingMessage != null, "This channel is expecting an incoming message from another channel that is also blocked waiting for an incoming message from us!");

				// It's permissible that the remote channel has already closed if it left a message
				// for us already.
				ErrorUtilities.VerifyInternal(!this.RemoteChannel.simulationCompleted || this.incomingMessage != null, "This channel is expecting an incoming message from another channel that has already been closed.");
				this.waitingForMessage = true;
			}

			this.incomingMessageSignal.WaitOne();

			lock (waitingForMessageCoordinationLock) {
				this.waitingForMessage = false;
				var response = this.incomingMessage;
				recipient = this.incomingMessageRecipient;
				this.incomingMessage = null;
				this.incomingMessageRecipient = null;
				return response;
			}
		}

		private void ProcessMessageFilter(IProtocolMessage message, bool outgoing) {
			if (outgoing) {
				if (this.outgoingMessageFilter != null) {
					this.outgoingMessageFilter(message);
				}
			} else {
				if (this.incomingMessageFilter != null) {
					this.incomingMessageFilter(message);
				}
			}
		}
	}
}