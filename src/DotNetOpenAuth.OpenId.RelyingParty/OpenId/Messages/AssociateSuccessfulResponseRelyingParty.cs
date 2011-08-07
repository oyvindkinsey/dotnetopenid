﻿//-----------------------------------------------------------------------
// <copyright file="AssociateSuccessfulResponseRelyingParty.cs" company="Andrew Arnott">
//     Copyright (c) Andrew Arnott. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace DotNetOpenAuth.OpenId.Messages {
	using System;
	using System.Collections.Generic;
	using System.Diagnostics.Contracts;
	using System.Linq;
	using System.Text;

	/// <summary>
	/// A successful association response as it is received by the relying party.
	/// </summary>
	[ContractClass(typeof(AssociateSuccessfulResponseRelyingPartyContract))]
	internal abstract class AssociateSuccessfulResponseRelyingParty : AssociateSuccessfulResponse {
		/// <summary>
		/// Initializes a new instance of the <see cref="AssociateSuccessfulResponseRelyingParty"/> class.
		/// </summary>
		/// <param name="version">The version.</param>
		/// <param name="request">The request.</param>
		internal AssociateSuccessfulResponseRelyingParty(Version version, AssociateRequest request)
			: base(version, request) {
		}

		/// <summary>
		/// Called to create the Association based on a request previously given by the Relying Party.
		/// </summary>
		/// <param name="request">The prior request for an association.</param>
		/// <returns>The created association.</returns>
		protected internal abstract Association CreateAssociationAtRelyingParty(AssociateRequest request);
	}
}