using System;
using System.Collections.Generic;

namespace SteamControllerGuard
{
    public sealed class RepairState
    {
        public bool IsValid()
        {
            DateTime latest = DateTime.UtcNow.AddMinutes(5);
            return Attempts >= 0 && Attempts <= 2 && LastAttemptUtc.ToUniversalTime() <= latest && LastManualUtc.ToUniversalTime() <= latest;
        }
        public string Fingerprint { get; set; }
        public int Attempts { get; set; }
        public DateTime LastAttemptUtc { get; set; }
        public string Outcome { get; set; }
        public string Detail { get; set; }
        public DateTime LastManualUtc { get; set; }
    }
    public sealed class Snapshot
    {
        public string At { get; set; }
        public string Version { get; set; }
        public string Status { get; set; }
        public bool SteamRunning { get; set; }
        public string SteamPath { get; set; }
        public bool OtherMapperRunning { get; set; }
        public string LastControllerEvent { get; set; }
        public string RepairService { get; set; }
        public RepairState Repair { get; set; }
        public List<UsbDevice> Devices { get; set; }
        public DateTime PausedUntil { get; set; }
        public ControllerObservation Controller { get; set; }
    }
}
