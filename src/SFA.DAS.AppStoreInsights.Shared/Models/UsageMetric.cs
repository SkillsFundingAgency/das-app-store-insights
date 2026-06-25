using System;

namespace SFA.DAS.AppStoreInsights.Shared.Models
{
    public class UsageMetric
    {
        public int AppId { get; set; }
        public byte VendorId { get; set; }
        public DateTime MetricDate { get; set; }
        public int Downloads { get; set; }
        public int Installs { get; set; }
        public int? Uninstalls { get; set; }
        public int? Sessions { get; set; }
        public string RawDataJson { get; set; }
    }
}