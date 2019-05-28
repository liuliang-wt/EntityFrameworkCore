// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.EntityFrameworkCore.Diagnostics
{
    public interface IDbCommandInterceptor
    {
        DbDataReader ReaderExecuting (DbCommand command, CommandEventData eventData);
        int? ScalarExecuting (DbCommand command, CommandEventData eventData);
        object NonQueryExecuting (DbCommand command, CommandEventData eventData);
        Task<DbDataReader> ReaderExecutingAsync (DbCommand command, CommandEventData eventData, CancellationToken cancellationToken = default);
        Task<int> ScalarExecutingAsync (DbCommand command, CommandEventData eventData, CancellationToken cancellationToken = default);
        Task<object> NonQueryExecutingAsync (DbCommand command, CommandEventData eventData, CancellationToken cancellationToken = default);
    }
}
