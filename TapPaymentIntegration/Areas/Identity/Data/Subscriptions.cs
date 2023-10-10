﻿using System.ComponentModel.DataAnnotations;

namespace TapPaymentIntegration.Areas.Identity.Data
{
    public class Subscriptions
    {
        [Key]
        public int SubscriptionId { get; set; }
        [Required]
        public string Name { get; set; }
        [Required]
        public string Currency { get; set; }
        public string Frequency { get; set; }
        [Required]
        public string Countries { get; set; }
        [Required]
        public string SetupFee { get; set; }
        [Required]
        public string Amount { get; set; }
        public string VAT { get; set; } 
        public bool Status { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}
