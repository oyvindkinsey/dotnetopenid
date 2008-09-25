﻿//-----------------------------------------------------------------------
// <copyright file="RsaSha1SigningBindingElement.cs" company="Andrew Arnott">
//     Copyright (c) Andrew Arnott. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace DotNetOAuth.ChannelElements {
	using System;
	using System.Security.Cryptography;
	using System.Text;

	/// <summary>
	/// A binding element that signs outgoing messages and verifies the signature on incoming messages.
	/// </summary>
	internal class RsaSha1SigningBindingElement : SigningBindingElementBase {
		/// <summary>
		/// Initializes a new instance of the <see cref="RsaSha1SigningBindingElement"/> class
		/// for use by Consumers.
		/// </summary>
		internal RsaSha1SigningBindingElement()
			: this(null) {
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="RsaSha1SigningBindingElement"/> class.
		/// </summary>
		/// <param name="signatureVerificationCallback">
		/// The delegate that will initialize the non-serialized properties necessary on a signed
		/// message so that its signature can be correctly calculated for verification.
		/// May be null for Consumers (who never have to verify signatures).
		/// </param>
		internal RsaSha1SigningBindingElement(Action<ITamperResistantOAuthMessage> signatureVerificationCallback)
			: base("RSA-SHA1", signatureVerificationCallback) {
		}

		/// <summary>
		/// Calculates a signature for a given message.
		/// </summary>
		/// <param name="message">The message to sign.</param>
		/// <returns>The signature for the message.</returns>
		/// <remarks>
		/// This method signs the message per OAuth 1.0 section 9.3.
		/// </remarks>
		protected override string GetSignature(ITamperResistantOAuthMessage message) {
			AsymmetricAlgorithm provider = new RSACryptoServiceProvider();
			AsymmetricSignatureFormatter hasher = new RSAPKCS1SignatureFormatter(provider);
			hasher.SetHashAlgorithm("SHA1");
			byte[] digest = hasher.CreateSignature(Encoding.ASCII.GetBytes(ConstructSignatureBaseString(message)));
			return Uri.EscapeDataString(Convert.ToBase64String(digest));
		}
	}
}
