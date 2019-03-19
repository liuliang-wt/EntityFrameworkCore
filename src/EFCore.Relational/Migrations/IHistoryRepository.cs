// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.EntityFrameworkCore.Migrations
{
    /// <summary>
    ///     <para>
    ///         An interface for the repository used to access the '__EFMigrationsHistory' table that tracks metadata
    ///         about EF Core Migrations such as which migrations have been applied.
    ///     </para>
    ///     <para>
    ///         Database providers typically implement this service by inheriting from <see cref="HistoryRepository" />.
    ///     </para>
    ///     <para>
    ///         The service lifetime is <see cref="ServiceLifetime.Scoped"/>. This means that each
    ///         <see cref="DbContext"/> instance will use its own instance of this service.
    ///         The implementation may depend on other services registered with any lifetime.
    ///         The implementation does not need to be thread-safe.
    ///     </para>
    /// </summary>
    public interface IHistoryRepository
    {
        /// <summary>
        ///     Checks whether or not the history table exists.
        /// </summary>
        /// <returns> <c>True</c> if the table already exists, <c>false</c> otherwise. </returns>
        bool Exists();

        /// <summary>
        ///     Checks whether or not the history table exists.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken" /> to observe while waiting for the task to complete.</param>
        /// <returns>
        ///     A task that represents the asynchronous operation. The task result contains
        ///     <c>True</c> if the table already exists, <c>false</c> otherwise.
        /// </returns>
        Task<bool> ExistsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        ///     Queries the history table for all migrations that have been applied.
        /// </summary>
        /// <returns> The list of applied migrations, as <see cref="HistoryRow" /> entities. </returns>
        IReadOnlyList<HistoryRow> GetAppliedMigrations();

        /// <summary>
        ///     Queries the history table for all migrations that have been applied.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken" /> to observe while waiting for the task to complete.</param>
        /// <returns>
        ///     A task that represents the asynchronous operation. The task result contains
        ///     the list of applied migrations, as <see cref="HistoryRow" /> entities.
        /// </returns>
        Task<IReadOnlyList<HistoryRow>> GetAppliedMigrationsAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        ///     Generates a SQL script that will create the history table.
        /// </summary>
        /// <returns> The SQL script. </returns>
        string GetCreateScript();

        /// <summary>
        ///     Generates a SQL script that will create the history table if and only if it does not already exist.
        /// </summary>
        /// <returns> The SQL script. </returns>
        string GetCreateIfNotExistsScript();

        /// <summary>
        ///     Generates a SQL script to insert a row into the history table.
        /// </summary>
        /// <param name="row"> The row to insert, represented as a <see cref="HistoryRow" /> entity. </param>
        /// <returns> The generated SQL. </returns>
        string GetInsertScript([NotNull] HistoryRow row);

        /// <summary>
        ///     Generates a SQL script to delete a row from the history table.
        /// </summary>
        /// <param name="migrationId"> The migration identifier of the row to delete. </param>
        /// <returns> The generated SQL. </returns>
        string GetDeleteScript([NotNull] string migrationId);

        /// <summary>
        ///     Generates a SQL Script that will <c>BEGIN</c> a block
        ///     of SQL if and only if the migration with the given identifier does not already exist in the history table.
        /// </summary>
        /// <param name="migrationId"> The migration identifier. </param>
        /// <returns> The generated SQL. </returns>
        string GetBeginIfNotExistsScript([NotNull] string migrationId);

        /// <summary>
        ///     Generates a SQL Script that will <c>BEGIN</c> a block
        ///     of SQL if and only if the migration with the given identifier already exists in the history table.
        /// </summary>
        /// <param name="migrationId"> The migration identifier. </param>
        /// <returns> The generated SQL. </returns>
        string GetBeginIfExistsScript([NotNull] string migrationId);

        /// <summary>
        ///     Generates a SQL script to <c>END</c> the SQL block.
        /// </summary>
        /// <returns> The generated SQL. </returns>
        string GetEndIfScript();
    }
}
