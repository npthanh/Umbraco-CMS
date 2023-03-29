﻿using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.DistributedLocking;
using Umbraco.Cms.Core.DistributedLocking.Exceptions;
using Umbraco.Cms.Core.Exceptions;
using Umbraco.Cms.Persistence.EFCore.Scoping;
using Umbraco.Extensions;

namespace Umbraco.Cms.Persistence.EFCore;

public class SqlServerEFCoreDistributedLockingMechanism : IDistributedLockingMechanism
{
    private readonly IOptionsMonitor<ConnectionStrings> _connectionStrings;
    private readonly IOptionsMonitor<GlobalSettings> _globalSettings;
    private readonly ILogger<SqlServerEFCoreDistributedLockingMechanism> _logger;
    private readonly Lazy<IEFCoreScopeAccessor> _scopeAccessor; // Hooray it's a circular dependency.

    /// <summary>
    ///     Initializes a new instance of the <see cref="SqlServerDistributedLockingMechanism" /> class.
    /// </summary>
    public SqlServerEFCoreDistributedLockingMechanism(
        ILogger<SqlServerEFCoreDistributedLockingMechanism> logger,
        Lazy<IEFCoreScopeAccessor> scopeAccessor,
        IOptionsMonitor<GlobalSettings> globalSettings,
        IOptionsMonitor<ConnectionStrings> connectionStrings)
    {
        _logger = logger;
        _scopeAccessor = scopeAccessor;
        _globalSettings = globalSettings;
        _connectionStrings = connectionStrings;
    }

    public bool HasActiveRelatedScope => _scopeAccessor.Value.AmbientScope is not null;

    /// <inheritdoc />
    public bool Enabled => _connectionStrings.CurrentValue.IsConnectionStringConfigured() &&
                           string.Equals(_connectionStrings.CurrentValue.ProviderName, "Microsoft.Data.SqlClient", StringComparison.InvariantCultureIgnoreCase) && _scopeAccessor.Value.AmbientScope is not null;

    /// <inheritdoc />
    public IDistributedLock ReadLock(int lockId, TimeSpan? obtainLockTimeout = null)
    {
        obtainLockTimeout ??= _globalSettings.CurrentValue.DistributedLockingReadLockDefaultTimeout;
        return new SqlServerDistributedLock(this, lockId, DistributedLockType.ReadLock, obtainLockTimeout.Value);
    }

    /// <inheritdoc />
    public IDistributedLock WriteLock(int lockId, TimeSpan? obtainLockTimeout = null)
    {
        obtainLockTimeout ??= _globalSettings.CurrentValue.DistributedLockingWriteLockDefaultTimeout;
        return new SqlServerDistributedLock(this, lockId, DistributedLockType.WriteLock, obtainLockTimeout.Value);
    }

    private class SqlServerDistributedLock : IDistributedLock
    {
        private readonly SqlServerEFCoreDistributedLockingMechanism _parent;
        private readonly TimeSpan _timeout;

        public SqlServerDistributedLock(
            SqlServerEFCoreDistributedLockingMechanism parent,
            int lockId,
            DistributedLockType lockType,
            TimeSpan timeout)
        {
            _parent = parent;
            _timeout = timeout;
            LockId = lockId;
            LockType = lockType;

            _parent._logger.LogDebug("Requesting {lockType} for id {id}", LockType, LockId);

            try
            {
                switch (lockType)
                {
                    case DistributedLockType.ReadLock:
                        ObtainReadLock();
                        break;
                    case DistributedLockType.WriteLock:
                        ObtainWriteLock();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(lockType), lockType, @"Unsupported lockType");
                }
            }
            catch (SqlException ex) when (ex.Number == 1205)
            {
                if (LockType == DistributedLockType.ReadLock)
                {
                    throw new DistributedReadLockTimeoutException(LockId);
                }

                throw new DistributedWriteLockTimeoutException(LockId);
            }

            _parent._logger.LogDebug("Acquired {lockType} for id {id}", LockType, LockId);
        }

        public int LockId { get; }

        public DistributedLockType LockType { get; }

        public void Dispose() =>
            // Mostly no op, cleaned up by completing transaction in scope.
            _parent._logger.LogDebug("Dropped {lockType} for id {id}", LockType, LockId);

        public override string ToString()
            => $"SqlServerDistributedLock({LockId}, {LockType}";

        private void ObtainReadLock()
        {
            IEfCoreScope? scope = _parent._scopeAccessor.Value.AmbientScope;

            if (scope is null)
            {
                throw new PanicException("No ambient scope");
            }

            scope.ExecuteWithContextAsync<Task>(async dbContext =>
            {
                if (dbContext.Database.CurrentTransaction is null)
                {
                    throw new InvalidOperationException(
                        "SqlServerDistributedLockingMechanism requires a transaction to function.");
                }

                if (dbContext.Database.CurrentTransaction.GetDbTransaction().IsolationLevel <
                    IsolationLevel.ReadCommitted)
                {
                    throw new InvalidOperationException(
                        "A transaction with minimum ReadCommitted isolation level is required.");
                }

                // FIXME: Use timeout variable
                await dbContext.Database.ExecuteSqlAsync($"SET LOCK_TIMEOUT 60000;");

                int? number = dbContext.UmbracoLocks.FromSqlRaw($"SELECT * FROM dbo.umbracoLock WITH (REPEATABLEREAD)").Select(x => x.Value).FirstOrDefault();

                if (number == null)
                {
                    // ensure we are actually locking!
                    throw new ArgumentException(@$"LockObject with id={LockId} does not exist.", nameof(LockId));
                }
            }).GetAwaiter().GetResult();
        }

        private void ObtainWriteLock()
        {
            IEfCoreScope? scope = _parent._scopeAccessor.Value.AmbientScope;
            if (scope is null)
            {
                throw new PanicException("No ambient scope");
            }

            scope.ExecuteWithContextAsync<Task>(async dbContext =>
            {
                if (dbContext.Database.CurrentTransaction is null)
                {
                    throw new InvalidOperationException(
                        "SqlServerDistributedLockingMechanism requires a transaction to function.");
                }

                if (dbContext.Database.CurrentTransaction.GetDbTransaction().IsolationLevel < IsolationLevel.ReadCommitted)
                {
                    throw new InvalidOperationException(
                        "A transaction with minimum ReadCommitted isolation level is required.");
                }

                // FIXME: Use variable timeout
                await dbContext.Database.ExecuteSqlAsync($"SET LOCK_TIMEOUT 60000;");

                var rowsAffected = await dbContext.Database.ExecuteSqlAsync(@$"UPDATE umbracoLock WITH (REPEATABLEREAD) SET value = (CASE WHEN (value=1) THEN -1 ELSE 1 END) WHERE id={LockId}");

                if (rowsAffected == 0)
                {
                    // ensure we are actually locking!
                    throw new ArgumentException($"LockObject with id={LockId} does not exist.");
                }
            }).GetAwaiter().GetResult();
        }
    }
}
