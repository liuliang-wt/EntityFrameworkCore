// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.EntityFrameworkCore.Diagnostics
{
    /// <summary>
    ///     <para>
    ///         Base implementation for all relational interceptors.
    ///     </para>
    ///     <para>
    ///         Relational providers that need to add interceptors should inherit from this class.
    ///         Non-Relational providers should inherit from <see cref="Interceptors"/>.
    ///     </para>
    ///     <para>
    ///         This type is typically used by database providers (and other extensions). It is generally
    ///         not used in application code.
    ///     </para>
    ///     <para>
    ///         The service lifetime is <see cref="ServiceLifetime.Scoped" />. This means that each
    ///         <see cref="DbContext" /> instance will use its own instance of this service.
    ///         The implementation may depend on other services registered with any lifetime.
    ///         The implementation does not need to be thread-safe.
    ///     </para>
    /// </summary>
    public class RelationalInterceptors : Interceptors, IRelationalInterceptors
    {
        /// <summary>
        ///     Creates a new <see cref="RelationalInterceptors"/> instance using the given dependencies.
        /// </summary>
        /// <param name="dependencies"> The dependencies for this service. </param>
        /// <param name="relationalDependencies"> The relational-specific dependencies for this service. </param>
        public RelationalInterceptors(
            [NotNull] InterceptorsDependencies dependencies,
            [NotNull] RelationalInterceptorsDependencies relationalDependencies)
            : base(dependencies)
        {
            Check.NotNull(relationalDependencies, nameof(relationalDependencies));

            // CommandInterceptor
            //     = dependencies.CurrentContext.GetDependencies(). Extensions
            //         .OfType<RelationalOptionsExtension>()
            //         .FirstOrDefault()
            //         ?.CommandInterceptor;
        }

        /// <summary>
        ///     The <see cref="IDbCommandInterceptor"/> registered, or null if none is registered.
        /// </summary>
        public virtual IDbCommandInterceptor CommandInterceptor { get; }
    }
}
