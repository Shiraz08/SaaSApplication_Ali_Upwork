using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Build.Framework;

namespace TapPaymentIntegration.Areas.Identity.Data;

// Add profile data for application users by adding properties to the ApplicationUser class
public class ApplicationUser : IdentityUser
{
    public string Password { get; set; }
    public string PaymentSource { get; set; }
    public string GYMName { get; set; }
    public string FullName { get; set; }
    public string UserType { get; set; }
    [Required]
    public string Frequency { get; set; } 
    public string PublicKey { get; set; } 
    public string VAT { get; set; } 
    public string SecertKey { get; set; } 
    public string MarchantID { get; set; } 
    public bool Status  { get; set; }
    public int SubscribeID { get; set; }
    [Required]
    public string Country { get; set; }
    public string City { get; set; }
    [Required]
    public string Currency { get; set; }
    public string Tap_CustomerID { get; set; }
    public string Tap_Subscription_ID { get; set; } 
    public string Tap_Agreement_ID { get; set; }
    public string Tap_Card_ID { get; set; }
    public string First_Six { get; set; }
    public string Last_Four { get; set; }
    public string Benefit_Invoice { get; set; }
}

