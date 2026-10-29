using System;
using UnityEngine;

namespace AppPofo.Presentation.Common
{
    public enum ProfileDataMode
    {
        Local,
        Cloud
    }

    public enum CloudConnectionStatus
    {
        Off,
        Connecting,
        ServerReachable,
        Offline
    }

    // The same Unity application chooses permissions from server-validated credentials.
    public enum CloudAccessRole
    {
        None,
        User,
        Administrator
    }

    // A mode decision is not permission to copy the user's entire SQLite database.
    // Device identity, account ownership and cloud CRUD arrive in S5-04B.
    public readonly struct CloudStartPlan
    {
        public readonly ProfileDataMode Mode;
        public readonly bool RequestConnectivityProbe;
        public readonly bool UploadExistingProfiles;

        public CloudStartPlan(bool serverEnabled)
        {
            Mode = serverEnabled ? ProfileDataMode.Cloud : ProfileDataMode.Local;
            RequestConnectivityProbe = serverEnabled;
            UploadExistingProfiles = false;
        }
    }

    public static class CloudModeSession
    {
        public static ProfileDataMode Mode { get; private set; } = ProfileDataMode.Local;
        public static CloudConnectionStatus Status { get; private set; } = CloudConnectionStatus.Off;
        public static CloudAccessRole AccessRole { get; private set; } = CloudAccessRole.None;

        // Never used as credentials for /api/profiles.
        public static string HealthUrl => CloudServerConfiguration.BaseUrl + "/api/health";

        public static CloudStartPlan Plan(bool serverEnabled) => new CloudStartPlan(serverEnabled);

        internal static void SetStartedMode(CloudStartPlan plan)
        {
            Mode = plan.Mode;
            AccessRole = CloudAccessRole.None;
            Status = plan.RequestConnectivityProbe
                ? CloudConnectionStatus.Connecting
                : CloudConnectionStatus.Off;
        }

        internal static void SetProbeResult(bool reachedServer,
            CloudAccessRole role = CloudAccessRole.User)
        {
            if (Mode != ProfileDataMode.Cloud) return;
            Status = reachedServer
                ? CloudConnectionStatus.ServerReachable
                : CloudConnectionStatus.Offline;
            AccessRole = reachedServer ? role : CloudAccessRole.None;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            Mode = ProfileDataMode.Local;
            Status = CloudConnectionStatus.Off;
            AccessRole = CloudAccessRole.None;
        }
    }
}
