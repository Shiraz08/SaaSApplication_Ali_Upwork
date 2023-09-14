using System.ComponentModel.DataAnnotations;

namespace TapPaymentIntegration.Areas.Identity.Data
{
    public class Subscriptions
    {
        [Key]
        public int SubscriptionId { get; set; }
        public string Name { get; set; }
        public string Currency { get; set; }
        public string Frequency { get; set; }
        public string Countries { get; set; }
        public string SetupFee { get; set; }
        public string Amount { get; set; }
        public bool Status { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}
