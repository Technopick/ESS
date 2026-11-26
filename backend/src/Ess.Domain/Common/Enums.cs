namespace Ess.Domain.Common;

/// <summary>Where an ESS transaction stands with NAV. A record is only Posted after NAV confirms it (rule 11).</summary>
public enum NavSyncStatus
{
    NotRequired = 0,
    PendingIntegration = 1,
    InProgress = 2,
    Posted = 3,
    Failed = 4,
    RequiresReconciliation = 5
}

/// <summary>Business status shared by requests that go through an approval workflow.</summary>
public enum RequestStatus
{
    Draft = 0,
    Submitted = 1,
    InApproval = 2,
    ReturnedForCorrection = 3,
    Approved = 4,
    Rejected = 5,
    Cancelled = 6,
    Withdrawn = 7,
    Completed = 8
}
