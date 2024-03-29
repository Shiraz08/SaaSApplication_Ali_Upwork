﻿using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using TapPaymentIntegration.Controllers;
using TapPaymentIntegration.Data;
using TapPaymentIntegration.Models.Card;
using TapPaymentIntegration.Models.Email;
using TapPaymentIntegration.Models.InvoiceDTO;
using TapPaymentIntegration.Models.PaymentDTO;
using TapPaymentIntegration.Utility;
using ApplicationUser = TapPaymentIntegration.Areas.Identity.Data.ApplicationUser;
using Order = TapPaymentIntegration.Models.InvoiceDTO.Order;

namespace TapPaymentIntegration.Models.HangFire
{
    public class DailyRecurreningJob
    {
        private readonly ILogger<HomeController> _logger;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private TapPaymentIntegrationContext _context;
        private readonly IUserStore<ApplicationUser> _userStore;
        private IWebHostEnvironment _environment;
        EmailSender _emailSender = new EmailSender();

        public DailyRecurreningJob(IWebHostEnvironment Environment, ILogger<HomeController> logger, SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager, TapPaymentIntegrationContext context, IUserStore<ApplicationUser> userStore)
        {
            _logger = logger;
            _signInManager = signInManager;
            _userManager = userManager;
            _context = context;
            _userStore = userStore;
            _environment = Environment;
        }
        public async Task AutoChargeJob()
        {
            CreateCharge deserialized_CreateCharge = null;
            var recurringCharges_list = _context.recurringCharges.Where(x => x.JobRunDate.Date == DateTime.UtcNow.Date && x.IsRun == false).ToList();
            foreach (var item in recurringCharges_list)
            {
                string[] result = item.ChargeId.Split('_').ToArray();
                if (result[0] == "chg")
                {
                    var getsubinfo = _context.subscriptions.Where(x => x.SubscriptionId == item.SubscriptionId).FirstOrDefault();
                    var getuserinfo = _context.Users.Where(x => x.Id == item.UserID).FirstOrDefault();
                    if (getuserinfo != null)
                    {
                        if (getuserinfo.SubscribeID > 0 && getuserinfo.Status == true)
                        {
                            string user_Email = getuserinfo.Email;
                            string attachmentTitle = $"{getuserinfo.FullName}_Invoice_Details";

                            //Save Code and get token
                            var client_savecard = new HttpClient();
                            var request_savecard = new HttpRequestMessage(HttpMethod.Post, "https://api.tap.company/v2/tokens");
                            request_savecard.Headers.Add("Authorization", "Bearer " + getuserinfo.SecertKey);
                            request_savecard.Headers.Add("accept", "application/json");
                            var content_savecard = new StringContent("\r\n{\r\n  \"saved_card\": {\r\n    \"card_id\": \"" + getuserinfo.Tap_Card_ID + "\",\r\n    \"customer_id\": \"" + getuserinfo.Tap_CustomerID + "\"\r\n  }\r\n}\r\n", null, "application/json");
                            request_savecard.Content = content_savecard;
                            var response_savecard = await client_savecard.SendAsync(request_savecard);
                            var result_savecard = await response_savecard.Content.ReadAsStringAsync();
                            Token Deserialized_savecard = JsonConvert.DeserializeObject<Token>(result_savecard);
                            int days = DateTime.DaysInMonth(DateTime.UtcNow.Year, DateTime.UtcNow.Month);
                            Random rnd = new Random();
                            var TransNo = "Txn_" + rnd.Next(10000000, 99999999);
                            var OrderNo = "Ord_" + rnd.Next(10000000, 99999999);
                            //Create Invoice 
                            InvoiceHelper.DailyRecurringJob_AutoChargeJobTotalCalculation(getuserinfo, getsubinfo, days, out decimal finalamount, out decimal Discount, out decimal Vat, out decimal sun_amount, out decimal after_vat_totalamount);

                            if (getuserinfo.Frequency == "DAILY")
                            {
                                // Create a charge
                                var client = new HttpClient();
                                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tap.company/v2/charges");
                                request.Headers.Add("Authorization", "Bearer " + getuserinfo.SecertKey);
                                request.Headers.Add("accept", "application/json");
                                var content = new StringContent("\r\n{\r\n  \"amount\": " + Decimal.Round(after_vat_totalamount, 2) + ",\r\n  \"currency\": \"" + getsubinfo.Currency + "\",\r\n  \"customer_initiated\": false,\r\n  \"threeDSecure\": true,\r\n  \"save_card\": false,\r\n  \"payment_agreement\": {\r\n    \"contract\": {\r\n      \"id\": \"" + getuserinfo.Tap_Card_ID + "\"\r\n    },\r\n    \"id\": \"" + getuserinfo.Tap_Agreement_ID + "\"\r\n  },\r\n  \"receipt\": {\r\n    \"email\": true,\r\n    \"sms\": true\r\n  },\"reference\": {\r\n    \"transaction\": \"" + TransNo + "\",\r\n    \"order\": \"" + OrderNo + "\"\r\n  },\r\n  \"customer\": {\r\n    \"id\": \"" + getuserinfo.Tap_CustomerID + "\"\r\n  },\r\n  \"source\": {\r\n    \"id\": \"" + Deserialized_savecard.id + "\"\r\n  },\r\n  \"redirect\": {\r\n    \"url\": \"https://test.com/\"\r\n  }\r\n}\r\n", null, "application/json");
                                request.Content = content;
                                var response = await client.SendAsync(request);
                                var bodys = await response.Content.ReadAsStringAsync();
                                deserialized_CreateCharge = JsonConvert.DeserializeObject<CreateCharge>(bodys);
                                if (deserialized_CreateCharge.status == "CAPTURED")
                                {
                                    Invoice invoice = new Invoice
                                    {
                                        InvoiceStartDate = DateTime.UtcNow,
                                        InvoiceEndDate = DateTime.UtcNow.AddDays(1),
                                        AddedDate = DateTime.UtcNow,
                                        AddedBy = getuserinfo.FullName,
                                        SubscriptionAmount = Convert.ToDouble(decimal.Round(after_vat_totalamount, 2)),
                                        Currency = getsubinfo.Currency,
                                        SubscriptionId = getsubinfo.SubscriptionId,
                                        Status = "Payment Captured",
                                        IsDeleted = false,
                                        VAT = Vat.ToString(),
                                        Discount = Discount.ToString(),
                                        Description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")",
                                        SubscriptionName = getsubinfo.Name,
                                        UserId = getuserinfo.Id,
                                        ChargeId = deserialized_CreateCharge.id,
                                        GymName = getuserinfo.GYMName,
                                        Country = getsubinfo.Countries
                                    };
                                    _context.invoices.Add(invoice);
                                    _context.SaveChanges();
                                    int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
                                    //Next Recurrening Job Date
                                    RecurringCharge recurringCharge = new RecurringCharge();
                                    recurringCharge.Amount = Convert.ToDecimal(item.Amount);
                                    recurringCharge.SubscriptionId = getsubinfo.SubscriptionId;
                                    recurringCharge.UserID = getuserinfo.Id;
                                    recurringCharge.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                    recurringCharge.ChargeId = deserialized_CreateCharge.id;
                                    recurringCharge.JobRunDate = invoice.InvoiceEndDate;
                                    recurringCharge.Invoice = "Inv" + max_invoice_id;
                                    _context.recurringCharges.Add(recurringCharge);
                                    _context.SaveChanges();


                                    // Update Job Table
                                    var recurreningjob = _context.recurringCharges.Where(x => x.RecurringChargeId == item.RecurringChargeId).FirstOrDefault();
                                    recurreningjob.IsRun = true;
                                    recurreningjob.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                    _context.recurringCharges.Update(recurreningjob);
                                    _context.SaveChanges();
                                    //Send Email
                                    var incoice_info = _context.invoices.Where(x => x.InvoiceId == max_invoice_id).FirstOrDefault();
                                    string body = string.Empty;
                                    _environment.WebRootPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                                    string contentRootPath = _environment.WebRootPath + "/htmltopdfRecurrening.html";
                                    string contentRootPath1 = _environment.WebRootPath + "/css/bootstrap.min.css";
                                    //Generate PDF
                                    using (StreamReader reader = new StreamReader(contentRootPath))
                                    {
                                        body = reader.ReadToEnd();
                                    }
                                    //Fill EMail By Parameter
                                    body = body.Replace("{title}", "Tamarran Payment Invoice");
                                    body = body.Replace("{currentdate}", DateTime.UtcNow.ToString("dd-MM-yyyy"));

                                    body = body.Replace("{InvocieStatus}", "Payment Captured");
                                    body = body.Replace("{InvoiceID}", "Inv" + max_invoice_id);


                                    body = body.Replace("{User_Name}", getuserinfo.FullName);
                                    body = body.Replace("{User_Email}", user_Email);
                                    body = body.Replace("{User_GYM}", getuserinfo.GYMName);
                                    body = body.Replace("{User_Phone}", getuserinfo.PhoneNumber);

                                    body = body.Replace("{SubscriptionName}", getsubinfo.Name);
                                    body = body.Replace("{Discount}", Discount.ToString());
                                    body = body.Replace("{SubscriptionPeriod}", getuserinfo.Frequency);
                                    //body = body.Replace("{SetupFee}", getsubinfo.SetupFee + " " + getsubinfo.Currency);
                                    body = body.Replace("{SetupFee}", "0.0" + " " + getsubinfo.Currency);
                                    int amount = Convert.ToInt32(incoice_info.SubscriptionAmount);
                                    body = body.Replace("{SubscriptionAmount}", decimal.Round(sun_amount, 2).ToString() + " " + getsubinfo.Currency);
                                    //Calculate VAT
                                    if (getsubinfo.VAT == null || getsubinfo.VAT == "0")
                                    {
                                        body = body.Replace("{VAT}", "0.00");
                                        body = body.Replace("{Total}", amount.ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{InvoiceAmount}", amount.ToString() + " " + getsubinfo.Currency);
                                        var without_vat = Convert.ToDecimal(finalamount);
                                        body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(without_vat), 2).ToString() + " " + getsubinfo.Currency);
                                    }
                                    else
                                    {
                                        body = body.Replace("{VAT}", decimal.Round(Convert.ToDecimal(Vat), 2).ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{Total}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{InvoiceAmount}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                        var without_vat = Convert.ToDecimal(finalamount);
                                        body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(without_vat), 2).ToString() + " " + getsubinfo.Currency);
                                    }
                                    var bytes = (new NReco.PdfGenerator.HtmlToPdfConverter()).GeneratePdf(body);
                                    var bodyemail = EmailBodyFill.EmailBodyForAutomaticPaymentConfirmation(getsubinfo, getuserinfo);
                                    _ = _emailSender.SendEmailWithFIle(bytes, user_Email, "Tamarran - Automatic Payment Confirmation", bodyemail, attachmentTitle);
                                }
                                else
                                {
                                    Invoice invoice = new Invoice
                                    {
                                        InvoiceStartDate = DateTime.UtcNow,
                                        InvoiceEndDate = DateTime.UtcNow.AddDays(1),
                                        AddedDate = DateTime.UtcNow,
                                        AddedBy = getuserinfo.FullName,
                                        SubscriptionAmount = Convert.ToDouble(decimal.Round(after_vat_totalamount, 2)),
                                        Currency = getsubinfo.Currency,
                                        SubscriptionId = getsubinfo.SubscriptionId,
                                        Status = "Un-Paid",
                                        IsDeleted = false,
                                        VAT = Vat.ToString(),
                                        Discount = Discount.ToString(),
                                        Description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")",
                                        SubscriptionName = getsubinfo.Name,
                                        UserId = getuserinfo.Id,
                                        ChargeId = deserialized_CreateCharge.id,
                                        GymName = getuserinfo.GYMName,
                                        Country = getsubinfo.Countries
                                    };
                                    _context.invoices.Add(invoice);
                                    _context.SaveChanges();
                                    int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
                                    var nameinvoice = "Inv" + max_invoice_id;

                                    var bodyemail = EmailBodyFill.EmailBodyForSubscriptionrenewalinTamarranfailed(getsubinfo, getuserinfo, nameinvoice, Constants.RedirectURL);
                                    _ = _emailSender.SendEmailAsync(user_Email, "Tamarran - Your subscription renewal in Tamarran failed", bodyemail);
                                }
                            }
                            else if (getuserinfo.Frequency == "WEEKLY")
                            {
                                // Create a charge
                                var client = new HttpClient();
                                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tap.company/v2/charges");
                                request.Headers.Add("Authorization", "Bearer " + getuserinfo.SecertKey);
                                request.Headers.Add("accept", "application/json");
                                var content = new StringContent("\r\n{\r\n  \"amount\": " + Decimal.Round(after_vat_totalamount, 2) + ",\r\n  \"currency\": \"" + getsubinfo.Currency + "\",\r\n  \"customer_initiated\": false,\r\n  \"threeDSecure\": true,\r\n  \"save_card\": false,\r\n  \"payment_agreement\": {\r\n    \"contract\": {\r\n      \"id\": \"" + getuserinfo.Tap_Card_ID + "\"\r\n    },\r\n    \"id\": \"" + getuserinfo.Tap_Agreement_ID + "\"\r\n  },\r\n  \"receipt\": {\r\n    \"email\": true,\r\n    \"sms\": true\r\n  },\"reference\": {\r\n    \"transaction\": \"" + TransNo + "\",\r\n    \"order\": \"" + OrderNo + "\"\r\n  },\r\n  \"customer\": {\r\n    \"id\": \"" + getuserinfo.Tap_CustomerID + "\"\r\n  },\r\n  \"source\": {\r\n    \"id\": \"" + Deserialized_savecard.id + "\"\r\n  },\r\n  \"redirect\": {\r\n    \"url\": \"https://1f3b186efe31e8696c144578816c5443.m.pipedream.net/\"\r\n  }\r\n}\r\n", null, "application/json");
                                request.Content = content;
                                var response = await client.SendAsync(request);
                                var bodys = await response.Content.ReadAsStringAsync();
                                deserialized_CreateCharge = JsonConvert.DeserializeObject<CreateCharge>(bodys);
                                if (deserialized_CreateCharge.status == "CAPTURED")
                                {
                                    Invoice invoice = new Invoice
                                    {
                                        InvoiceStartDate = DateTime.UtcNow,
                                        InvoiceEndDate = DateTime.UtcNow.AddDays(7),
                                        Currency = getsubinfo.Currency,
                                        AddedDate = DateTime.UtcNow,
                                        AddedBy = getuserinfo.FullName,
                                        SubscriptionAmount = Convert.ToDouble(decimal.Round(after_vat_totalamount, 2)),
                                        VAT = Vat.ToString(),
                                        Discount = Discount.ToString(),
                                        SubscriptionId = getsubinfo.SubscriptionId,
                                        Status = "Payment Captured",
                                        IsDeleted = false,
                                        Description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")",
                                        SubscriptionName = getsubinfo.Name,
                                        UserId = getuserinfo.Id,
                                        ChargeId = deserialized_CreateCharge.id,
                                        GymName = getuserinfo.GYMName,
                                        Country = getsubinfo.Countries
                                    };
                                    _context.invoices.Add(invoice);
                                    _context.SaveChanges();
                                    int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
                                    //Next Recurrening Job Date
                                    RecurringCharge recurringCharge = new RecurringCharge();
                                    recurringCharge.Amount = Convert.ToDecimal(item.Amount);
                                    recurringCharge.SubscriptionId = getsubinfo.SubscriptionId;
                                    recurringCharge.UserID = getuserinfo.Id;
                                    recurringCharge.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                    recurringCharge.ChargeId = deserialized_CreateCharge.id;
                                    recurringCharge.JobRunDate = invoice.InvoiceEndDate.AddDays(1);
                                    recurringCharge.Invoice = "Inv" + max_invoice_id;
                                    _context.recurringCharges.Add(recurringCharge);
                                    _context.SaveChanges();

                                    // Update Job Table
                                    var recurreningjob = _context.recurringCharges.Where(x => x.RecurringChargeId == item.RecurringChargeId).FirstOrDefault();
                                    recurreningjob.IsRun = true;
                                    recurreningjob.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                    _context.recurringCharges.Update(recurreningjob);
                                    _context.SaveChanges();
                                    //Send Email
                                    var incoice_info = _context.invoices.Where(x => x.InvoiceId == max_invoice_id).FirstOrDefault();
                                    string body = string.Empty;
                                    _environment.WebRootPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                                    string contentRootPath = _environment.WebRootPath + "/htmltopdfRecurrening.html";
                                    string contentRootPath1 = _environment.WebRootPath + "/css/bootstrap.min.css";
                                    //Generate PDF
                                    using (StreamReader reader = new StreamReader(contentRootPath))
                                    {
                                        body = reader.ReadToEnd();
                                    }
                                    //Fill EMail By Parameter
                                    body = body.Replace("{title}", "Tamarran Payment Invoice");
                                    body = body.Replace("{currentdate}", DateTime.UtcNow.ToString("dd-MM-yyyy"));

                                    body = body.Replace("{InvocieStatus}", "Payment Captured");
                                    body = body.Replace("{InvoiceID}", "Inv" + max_invoice_id);


                                    body = body.Replace("{User_Name}", getuserinfo.FullName);
                                    body = body.Replace("{User_Email}", user_Email);
                                    body = body.Replace("{User_GYM}", getuserinfo.GYMName);
                                    body = body.Replace("{User_Phone}", getuserinfo.PhoneNumber);

                                    body = body.Replace("{SubscriptionName}", getsubinfo.Name);
                                    body = body.Replace("{Discount}", Discount.ToString());
                                    body = body.Replace("{SubscriptionPeriod}", getuserinfo.Frequency);
                                    body = body.Replace("{SetupFee}", "0.0" + " " + getsubinfo.Currency);
                                    int amount = Convert.ToInt32(incoice_info.SubscriptionAmount);
                                    body = body.Replace("{SubscriptionAmount}", decimal.Round(sun_amount, 2).ToString() + " " + getsubinfo.Currency);
                                    //Calculate VAT
                                    if (getsubinfo.VAT == null || getsubinfo.VAT == "0")
                                    {
                                        body = body.Replace("{VAT}", "0.00");
                                        body = body.Replace("{Total}", amount.ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{InvoiceAmount}", amount.ToString() + " " + getsubinfo.Currency);
                                        var without_vat = Convert.ToDecimal(finalamount);
                                        body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(without_vat), 2).ToString() + " " + getsubinfo.Currency);
                                    }
                                    else
                                    {
                                        body = body.Replace("{VAT}", decimal.Round(Convert.ToDecimal(Vat), 2).ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{Total}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{InvoiceAmount}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                        var without_vat = Convert.ToDecimal(finalamount);
                                        body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(without_vat), 2).ToString() + " " + getsubinfo.Currency);
                                    }
                                    var bytes = (new NReco.PdfGenerator.HtmlToPdfConverter()).GeneratePdf(body);
                                    var bodyemail = EmailBodyFill.EmailBodyForAutomaticPaymentConfirmation(getsubinfo, getuserinfo);
                                    _ = _emailSender.SendEmailWithFIle(bytes, user_Email, "Tamarran - Automatic Payment Confirmation", bodyemail, attachmentTitle);
                                }
                                else
                                {
                                    Invoice invoice = new Invoice
                                    {
                                        InvoiceStartDate = DateTime.UtcNow,
                                        InvoiceEndDate = DateTime.UtcNow.AddDays(7),
                                        Currency = getsubinfo.Currency,
                                        AddedDate = DateTime.UtcNow,
                                        AddedBy = getuserinfo.FullName,
                                        SubscriptionAmount = Convert.ToDouble(decimal.Round(after_vat_totalamount, 2)),
                                        VAT = Vat.ToString(),
                                        Discount = Discount.ToString(),
                                        SubscriptionId = getsubinfo.SubscriptionId,
                                        Status = "Un-Paid",
                                        IsDeleted = false,
                                        Description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")",
                                        SubscriptionName = getsubinfo.Name,
                                        UserId = getuserinfo.Id,
                                        ChargeId = deserialized_CreateCharge.id,
                                        GymName = getuserinfo.GYMName,
                                        Country = getsubinfo.Countries
                                    };
                                    _context.invoices.Add(invoice);
                                    _context.SaveChanges();
                                    int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
                                    var nameinvoice = "Inv" + max_invoice_id;
                                    var bodyemail = EmailBodyFill.EmailBodyForSubscriptionrenewalinTamarranfailed(getsubinfo, getuserinfo, nameinvoice, Constants.RedirectURL);
                                    _ = _emailSender.SendEmailAsync(user_Email, "Tamarran - Your subscription renewal in Tamarran failed", bodyemail);
                                }
                            }
                            else if (getuserinfo.Frequency == "MONTHLY")
                            {
                                // Create a charge
                                var client = new HttpClient();
                                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tap.company/v2/charges");
                                request.Headers.Add("Authorization", "Bearer " + getuserinfo.SecertKey);
                                request.Headers.Add("accept", "application/json");
                                var content = new StringContent("\r\n{\r\n  \"amount\": " + Decimal.Round(after_vat_totalamount, 2) + ",\r\n  \"currency\": \"" + getsubinfo.Currency + "\",\r\n  \"customer_initiated\": false,\r\n  \"threeDSecure\": true,\r\n  \"save_card\": false,\r\n  \"payment_agreement\": {\r\n    \"contract\": {\r\n      \"id\": \"" + getuserinfo.Tap_Card_ID + "\"\r\n    },\r\n    \"id\": \"" + getuserinfo.Tap_Agreement_ID + "\"\r\n  },\r\n  \"receipt\": {\r\n    \"email\": true,\r\n    \"sms\": true\r\n  },\"reference\": {\r\n    \"transaction\": \"" + TransNo + "\",\r\n    \"order\": \"" + OrderNo + "\"\r\n  },\r\n  \"customer\": {\r\n    \"id\": \"" + getuserinfo.Tap_CustomerID + "\"\r\n  },\r\n  \"source\": {\r\n    \"id\": \"" + Deserialized_savecard.id + "\"\r\n  },\r\n  \"redirect\": {\r\n    \"url\": \"https://1f3b186efe31e8696c144578816c5443.m.pipedream.net/\"\r\n  }\r\n}\r\n", null, "application/json");
                                request.Content = content;
                                var response = await client.SendAsync(request);
                                var bodys = await response.Content.ReadAsStringAsync();
                                deserialized_CreateCharge = JsonConvert.DeserializeObject<CreateCharge>(bodys);
                                if (deserialized_CreateCharge.status == "CAPTURED")
                                {
                                    Invoice invoice = new Invoice
                                    {
                                        InvoiceStartDate = DateTime.UtcNow,
                                        InvoiceEndDate = DateTime.UtcNow.AddMonths(1),
                                        AddedDate = DateTime.UtcNow,
                                        AddedBy = getuserinfo.FullName,
                                        SubscriptionAmount = Convert.ToDouble(decimal.Round(after_vat_totalamount, 2)),
                                        SubscriptionId = getsubinfo.SubscriptionId,
                                        Currency = getsubinfo.Currency,
                                        Status = "Payment Captured",
                                        IsDeleted = false,
                                        VAT = Vat.ToString(),
                                        Discount = Discount.ToString(),
                                        Description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")",
                                        SubscriptionName = getsubinfo.Name,
                                        UserId = getuserinfo.Id,
                                        ChargeId = deserialized_CreateCharge.id,
                                        GymName = getuserinfo.GYMName,
                                        Country = getsubinfo.Countries
                                    };
                                    _context.invoices.Add(invoice);
                                    _context.SaveChanges();
                                    int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
                                    //Next Recurrening Job Date
                                    RecurringCharge recurringCharge = new RecurringCharge();
                                    recurringCharge.Amount = Convert.ToDecimal(item.Amount);
                                    recurringCharge.SubscriptionId = getsubinfo.SubscriptionId;
                                    recurringCharge.UserID = getuserinfo.Id;
                                    recurringCharge.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                    recurringCharge.ChargeId = deserialized_CreateCharge.id;
                                    recurringCharge.JobRunDate = invoice.InvoiceEndDate.AddDays(1);
                                    _context.recurringCharges.Add(recurringCharge);
                                    recurringCharge.Invoice = "Inv" + max_invoice_id;
                                    _context.SaveChanges();

                                    // Update Job Table
                                    var recurreningjob = _context.recurringCharges.Where(x => x.RecurringChargeId == item.RecurringChargeId).FirstOrDefault();
                                    recurreningjob.IsRun = true;
                                    recurreningjob.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                    _context.recurringCharges.Update(recurreningjob);
                                    _context.SaveChanges();
                                    //Send Email
                                    var incoice_info = _context.invoices.Where(x => x.InvoiceId == max_invoice_id).FirstOrDefault();
                                    string body = string.Empty;
                                    _environment.WebRootPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                                    string contentRootPath = _environment.WebRootPath + "/htmltopdfRecurrening.html";
                                    string contentRootPath1 = _environment.WebRootPath + "/css/bootstrap.min.css";
                                    //Generate PDF
                                    using (StreamReader reader = new StreamReader(contentRootPath))
                                    {
                                        body = reader.ReadToEnd();
                                    }
                                    //Fill EMail By Parameter
                                    body = body.Replace("{title}", "Tamarran Payment Invoice");
                                    body = body.Replace("{currentdate}", DateTime.UtcNow.ToString("dd-MM-yyyy"));

                                    body = body.Replace("{InvocieStatus}", "Payment Captured");
                                    body = body.Replace("{InvoiceID}", "Inv" + max_invoice_id);


                                    body = body.Replace("{User_Name}", getuserinfo.FullName);
                                    body = body.Replace("{User_Email}", user_Email);
                                    body = body.Replace("{User_GYM}", getuserinfo.GYMName);
                                    body = body.Replace("{User_Phone}", getuserinfo.PhoneNumber);

                                    body = body.Replace("{SubscriptionName}", getsubinfo.Name);
                                    body = body.Replace("{Discount}", Discount.ToString());
                                    body = body.Replace("{SubscriptionPeriod}", getuserinfo.Frequency);
                                    body = body.Replace("{SetupFee}", "0.0" + " " + getsubinfo.Currency);
                                    int amount = Convert.ToInt32(incoice_info.SubscriptionAmount);
                                    body = body.Replace("{SubscriptionAmount}", decimal.Round(sun_amount, 2).ToString() + " " + getsubinfo.Currency);
                                    //Calculate VAT
                                    if (getsubinfo.VAT == null || getsubinfo.VAT == "0")
                                    {
                                        body = body.Replace("{VAT}", "0.00");
                                        body = body.Replace("{Total}", amount.ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{InvoiceAmount}", amount.ToString() + " " + getsubinfo.Currency);
                                        var without_vat = Convert.ToDecimal(finalamount);
                                        body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(without_vat), 2).ToString() + " " + getsubinfo.Currency);
                                    }
                                    else
                                    {
                                        body = body.Replace("{VAT}", decimal.Round(Convert.ToDecimal(Vat), 2).ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{Total}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{InvoiceAmount}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                        var without_vat = Convert.ToDecimal(finalamount);
                                        body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(without_vat), 2).ToString() + " " + getsubinfo.Currency);
                                    }
                                    var bytes = (new NReco.PdfGenerator.HtmlToPdfConverter()).GeneratePdf(body);
                                    var bodyemail = EmailBodyFill.EmailBodyForAutomaticPaymentConfirmation(getsubinfo, getuserinfo);
                                    _ = _emailSender.SendEmailWithFIle(bytes, user_Email, "Tamarran - Automatic Payment Confirmation", bodyemail, attachmentTitle);
                                }
                                else
                                {
                                    Invoice invoice = new Invoice
                                    {
                                        InvoiceStartDate = DateTime.UtcNow,
                                        InvoiceEndDate = DateTime.UtcNow.AddMonths(1),
                                        AddedDate = DateTime.UtcNow,
                                        AddedBy = getuserinfo.FullName,
                                        SubscriptionAmount = Convert.ToDouble(decimal.Round(after_vat_totalamount, 2)),
                                        SubscriptionId = getsubinfo.SubscriptionId,
                                        Currency = getsubinfo.Currency,
                                        Status = "Un-Paid",
                                        IsDeleted = false,
                                        VAT = Vat.ToString(),
                                        Discount = Discount.ToString(),
                                        Description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")",
                                        SubscriptionName = getsubinfo.Name,
                                        UserId = getuserinfo.Id,
                                        ChargeId = deserialized_CreateCharge.id,
                                        GymName = getuserinfo.GYMName,
                                        Country = getsubinfo.Countries
                                    };
                                    _context.invoices.Add(invoice);
                                    _context.SaveChanges();
                                    int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
                                    var nameinvoice = "Inv" + max_invoice_id;
                                    var bodyemail = EmailBodyFill.EmailBodyForSubscriptionrenewalinTamarranfailed(getsubinfo, getuserinfo, nameinvoice, Constants.RedirectURL);
                                    _ = _emailSender.SendEmailAsync(user_Email, "Tamarran - Your subscription renewal in Tamarran failed", bodyemail);
                                }
                            }
                            else if (getuserinfo.Frequency == "QUARTERLY")
                            {
                                // Create a charge
                                var client = new HttpClient();
                                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tap.company/v2/charges");
                                request.Headers.Add("Authorization", "Bearer " + getuserinfo.SecertKey);
                                request.Headers.Add("accept", "application/json");
                                var content = new StringContent("\r\n{\r\n  \"amount\": " + Decimal.Round(after_vat_totalamount, 2) + ",\r\n  \"currency\": \"" + getsubinfo.Currency + "\",\r\n  \"customer_initiated\": false,\r\n  \"threeDSecure\": true,\r\n  \"save_card\": false,\r\n  \"payment_agreement\": {\r\n    \"contract\": {\r\n      \"id\": \"" + getuserinfo.Tap_Card_ID + "\"\r\n    },\r\n    \"id\": \"" + getuserinfo.Tap_Agreement_ID + "\"\r\n  },\r\n  \"receipt\": {\r\n    \"email\": true,\r\n    \"sms\": true\r\n  },\"reference\": {\r\n    \"transaction\": \"" + TransNo + "\",\r\n    \"order\": \"" + OrderNo + "\"\r\n  },\r\n  \"customer\": {\r\n    \"id\": \"" + getuserinfo.Tap_CustomerID + "\"\r\n  },\r\n  \"source\": {\r\n    \"id\": \"" + Deserialized_savecard.id + "\"\r\n  },\r\n  \"redirect\": {\r\n    \"url\": \"https://1f3b186efe31e8696c144578816c5443.m.pipedream.net/\"\r\n  }\r\n}\r\n", null, "application/json");
                                request.Content = content;
                                var response = await client.SendAsync(request);
                                var bodys = await response.Content.ReadAsStringAsync();
                                deserialized_CreateCharge = JsonConvert.DeserializeObject<CreateCharge>(bodys);
                                if (deserialized_CreateCharge.status == "CAPTURED")
                                {
                                    Invoice invoice = new Invoice
                                    {
                                        InvoiceStartDate = DateTime.UtcNow,
                                        InvoiceEndDate = DateTime.UtcNow.AddMonths(3),
                                        AddedDate = DateTime.UtcNow,
                                        AddedBy = getuserinfo.FullName,
                                        Currency = getsubinfo.Currency,
                                        SubscriptionAmount = Convert.ToDouble(decimal.Round(after_vat_totalamount, 2)),
                                        SubscriptionId = getsubinfo.SubscriptionId,
                                        Status = "Payment Captured",
                                        VAT = Vat.ToString(),
                                        Discount = Discount.ToString(),
                                        IsDeleted = false,
                                        Description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")",
                                        SubscriptionName = getsubinfo.Name,
                                        UserId = getuserinfo.Id,
                                        ChargeId = deserialized_CreateCharge.id,
                                        GymName = getuserinfo.GYMName,
                                        Country = getsubinfo.Countries
                                    };
                                    _context.invoices.Add(invoice);
                                    _context.SaveChanges();
                                    int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
                                    //Next Recurrening Job Date
                                    RecurringCharge recurringCharge = new RecurringCharge();
                                    recurringCharge.Amount = Convert.ToDecimal(item.Amount);
                                    recurringCharge.SubscriptionId = getsubinfo.SubscriptionId;
                                    recurringCharge.UserID = getuserinfo.Id;
                                    recurringCharge.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                    recurringCharge.ChargeId = deserialized_CreateCharge.id;
                                    recurringCharge.JobRunDate = invoice.InvoiceEndDate.AddDays(1);
                                    recurringCharge.Invoice = "Inv" + max_invoice_id;
                                    _context.recurringCharges.Add(recurringCharge);
                                    _context.SaveChanges();

                                    // Update Job Table
                                    var recurreningjob = _context.recurringCharges.Where(x => x.RecurringChargeId == item.RecurringChargeId).FirstOrDefault();
                                    recurreningjob.IsRun = true;
                                    recurreningjob.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                    _context.recurringCharges.Update(recurreningjob);
                                    _context.SaveChanges();
                                    //Send Email
                                    var incoice_info = _context.invoices.Where(x => x.InvoiceId == max_invoice_id).FirstOrDefault();
                                    string body = string.Empty;
                                    _environment.WebRootPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                                    string contentRootPath = _environment.WebRootPath + "/htmltopdfRecurrening.html";
                                    string contentRootPath1 = _environment.WebRootPath + "/css/bootstrap.min.css";
                                    //Generate PDF
                                    using (StreamReader reader = new StreamReader(contentRootPath))
                                    {
                                        body = reader.ReadToEnd();
                                    }
                                    //Fill EMail By Parameter
                                    body = body.Replace("{title}", "Tamarran Payment Invoice");
                                    body = body.Replace("{currentdate}", DateTime.UtcNow.ToString("dd-MM-yyyy"));

                                    body = body.Replace("{InvocieStatus}", "Payment Captured");
                                    body = body.Replace("{InvoiceID}", "Inv" + max_invoice_id);


                                    body = body.Replace("{User_Name}", getuserinfo.FullName);
                                    body = body.Replace("{User_Email}", user_Email);
                                    body = body.Replace("{User_GYM}", getuserinfo.GYMName);
                                    body = body.Replace("{User_Phone}", getuserinfo.PhoneNumber);

                                    body = body.Replace("{SubscriptionName}", getsubinfo.Name);
                                    body = body.Replace("{Discount}", Discount.ToString());
                                    body = body.Replace("{SubscriptionPeriod}", getuserinfo.Frequency);
                                    body = body.Replace("{SetupFee}", "0.0" + " " + getsubinfo.Currency);
                                    int amount = Convert.ToInt32(incoice_info.SubscriptionAmount);
                                    body = body.Replace("{SubscriptionAmount}", decimal.Round(sun_amount, 2).ToString() + " " + getsubinfo.Currency);
                                    //Calculate VAT
                                    if (getsubinfo.VAT == null || getsubinfo.VAT == "0")
                                    {
                                        body = body.Replace("{VAT}", "0.00");
                                        body = body.Replace("{Total}", amount.ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{InvoiceAmount}", amount.ToString() + " " + getsubinfo.Currency);
                                        var without_vat = Convert.ToDecimal(finalamount);
                                        body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(without_vat), 2).ToString() + " " + getsubinfo.Currency);
                                    }
                                    else
                                    {
                                        body = body.Replace("{VAT}", decimal.Round(Convert.ToDecimal(Vat), 2).ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{Total}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{InvoiceAmount}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                        var without_vat = Convert.ToDecimal(finalamount);
                                        body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(without_vat), 2).ToString() + " " + getsubinfo.Currency);
                                    }
                                    var bytes = (new NReco.PdfGenerator.HtmlToPdfConverter()).GeneratePdf(body);
                                    var bodyemail = EmailBodyFill.EmailBodyForAutomaticPaymentConfirmation(getsubinfo, getuserinfo);
                                    _ = _emailSender.SendEmailWithFIle(bytes, user_Email, "Tamarran - Automatic Payment Confirmation", bodyemail, attachmentTitle);
                                }
                                else
                                {
                                    Invoice invoice = new Invoice
                                    {
                                        InvoiceStartDate = DateTime.UtcNow,
                                        InvoiceEndDate = DateTime.UtcNow.AddMonths(3),
                                        AddedDate = DateTime.UtcNow,
                                        AddedBy = getuserinfo.FullName,
                                        Currency = getsubinfo.Currency,
                                        SubscriptionAmount = Convert.ToDouble(decimal.Round(after_vat_totalamount, 2)),
                                        SubscriptionId = getsubinfo.SubscriptionId,
                                        Status = "Un-Paid",
                                        VAT = Vat.ToString(),
                                        Discount = Discount.ToString(),
                                        IsDeleted = false,
                                        Description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")",
                                        SubscriptionName = getsubinfo.Name,
                                        UserId = getuserinfo.Id,
                                        ChargeId = deserialized_CreateCharge.id,
                                        GymName = getuserinfo.GYMName,
                                        Country = getsubinfo.Countries
                                    };
                                    _context.invoices.Add(invoice);
                                    _context.SaveChanges();
                                    int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
                                    var nameinvoice = "Inv" + max_invoice_id;
                                    var bodyemail = EmailBodyFill.EmailBodyForSubscriptionrenewalinTamarranfailed(getsubinfo, getuserinfo, nameinvoice, Constants.RedirectURL);
                                    _ = _emailSender.SendEmailAsync(user_Email, "Tamarran - Your subscription renewal in Tamarran failed", bodyemail);
                                }
                            }
                            else if (getuserinfo.Frequency == "HALFYEARLY")
                            {
                                // Create a charge
                                var client = new HttpClient();
                                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tap.company/v2/charges");
                                request.Headers.Add("Authorization", "Bearer " + getuserinfo.SecertKey);
                                request.Headers.Add("accept", "application/json");
                                var content = new StringContent("\r\n{\r\n  \"amount\": " + Decimal.Round(after_vat_totalamount, 2) + ",\r\n  \"currency\": \"" + getsubinfo.Currency + "\",\r\n  \"customer_initiated\": false,\r\n  \"threeDSecure\": true,\r\n  \"save_card\": false,\r\n  \"payment_agreement\": {\r\n    \"contract\": {\r\n      \"id\": \"" + getuserinfo.Tap_Card_ID + "\"\r\n    },\r\n    \"id\": \"" + getuserinfo.Tap_Agreement_ID + "\"\r\n  },\r\n  \"receipt\": {\r\n    \"email\": true,\r\n    \"sms\": true\r\n  },\"reference\": {\r\n    \"transaction\": \"" + TransNo + "\",\r\n    \"order\": \"" + OrderNo + "\"\r\n  },\r\n  \"customer\": {\r\n    \"id\": \"" + getuserinfo.Tap_CustomerID + "\"\r\n  },\r\n  \"source\": {\r\n    \"id\": \"" + Deserialized_savecard.id + "\"\r\n  },\r\n  \"redirect\": {\r\n    \"url\": \"https://1f3b186efe31e8696c144578816c5443.m.pipedream.net/\"\r\n  }\r\n}\r\n", null, "application/json");
                                request.Content = content;
                                var response = await client.SendAsync(request);
                                var bodys = await response.Content.ReadAsStringAsync();
                                deserialized_CreateCharge = JsonConvert.DeserializeObject<CreateCharge>(bodys);
                                if (deserialized_CreateCharge.status == "CAPTURED")
                                {
                                    Invoice invoice = new Invoice
                                    {
                                        InvoiceStartDate = DateTime.UtcNow,
                                        InvoiceEndDate = DateTime.UtcNow.AddMonths(6),
                                        AddedDate = DateTime.UtcNow,
                                        VAT = Vat.ToString(),
                                        Discount = Discount.ToString(),
                                        AddedBy = getuserinfo.FullName,
                                        SubscriptionAmount = Convert.ToDouble(decimal.Round(after_vat_totalamount, 2)),
                                        Currency = getsubinfo.Currency,
                                        SubscriptionId = getsubinfo.SubscriptionId,
                                        Status = "Payment Captured",
                                        IsDeleted = false,
                                        Description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")",
                                        SubscriptionName = getsubinfo.Name,
                                        UserId = getuserinfo.Id,
                                        ChargeId = deserialized_CreateCharge.id,
                                        GymName = getuserinfo.GYMName,
                                        Country = getsubinfo.Countries
                                    };
                                    _context.invoices.Add(invoice);
                                    _context.SaveChanges();
                                    int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
                                    //Next Recurrening Job Date
                                    RecurringCharge recurringCharge = new RecurringCharge();
                                    recurringCharge.Amount = Convert.ToDecimal(item.Amount);
                                    recurringCharge.SubscriptionId = getsubinfo.SubscriptionId;
                                    recurringCharge.UserID = getuserinfo.Id;
                                    recurringCharge.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                    recurringCharge.ChargeId = deserialized_CreateCharge.id;
                                    recurringCharge.JobRunDate = invoice.InvoiceEndDate.AddDays(1);
                                    recurringCharge.Invoice = "Inv" + max_invoice_id;
                                    _context.recurringCharges.Add(recurringCharge);
                                    _context.SaveChanges();

                                    // Update Job Table
                                    var recurreningjob = _context.recurringCharges.Where(x => x.RecurringChargeId == item.RecurringChargeId).FirstOrDefault();
                                    recurreningjob.IsRun = true;
                                    recurreningjob.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                    _context.recurringCharges.Update(recurreningjob);
                                    _context.SaveChanges();
                                    //Send Email
                                    var incoice_info = _context.invoices.Where(x => x.InvoiceId == max_invoice_id).FirstOrDefault();
                                    string body = string.Empty;
                                    _environment.WebRootPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                                    string contentRootPath = _environment.WebRootPath + "/htmltopdfRecurrening.html";
                                    string contentRootPath1 = _environment.WebRootPath + "/css/bootstrap.min.css";
                                    //Generate PDF
                                    using (StreamReader reader = new StreamReader(contentRootPath))
                                    {
                                        body = reader.ReadToEnd();
                                    }
                                    //Fill EMail By Parameter
                                    body = body.Replace("{title}", "Tamarran Payment Invoice");
                                    body = body.Replace("{currentdate}", DateTime.UtcNow.ToString("dd-MM-yyyy"));

                                    body = body.Replace("{InvocieStatus}", "Payment Captured");
                                    body = body.Replace("{InvoiceID}", "Inv" + max_invoice_id);


                                    body = body.Replace("{User_Name}", getuserinfo.FullName);
                                    body = body.Replace("{User_Email}", user_Email);
                                    body = body.Replace("{User_GYM}", getuserinfo.GYMName);
                                    body = body.Replace("{User_Phone}", getuserinfo.PhoneNumber);

                                    body = body.Replace("{SubscriptionName}", getsubinfo.Name);
                                    body = body.Replace("{Discount}", Discount.ToString());
                                    body = body.Replace("{SubscriptionPeriod}", getuserinfo.Frequency);
                                    body = body.Replace("{SetupFee}", "0.0" + " " + getsubinfo.Currency);
                                    int amount = Convert.ToInt32(incoice_info.SubscriptionAmount);
                                    body = body.Replace("{SubscriptionAmount}", decimal.Round(sun_amount, 2).ToString() + " " + getsubinfo.Currency);
                                    //Calculate VAT
                                    if (getsubinfo.VAT == null || getsubinfo.VAT == "0")
                                    {
                                        body = body.Replace("{VAT}", "0.00");
                                        body = body.Replace("{Total}", amount.ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{InvoiceAmount}", amount.ToString() + " " + getsubinfo.Currency);
                                        var without_vat = Convert.ToDecimal(finalamount);
                                        body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(without_vat), 2).ToString() + " " + getsubinfo.Currency);
                                    }
                                    else
                                    {
                                        body = body.Replace("{VAT}", decimal.Round(Convert.ToDecimal(Vat), 2).ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{Total}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{InvoiceAmount}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                        var without_vat = Convert.ToDecimal(finalamount);
                                        body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(without_vat), 2).ToString() + " " + getsubinfo.Currency);
                                    }
                                    var bytes = (new NReco.PdfGenerator.HtmlToPdfConverter()).GeneratePdf(body);
                                    var bodyemail = EmailBodyFill.EmailBodyForAutomaticPaymentConfirmation(getsubinfo, getuserinfo);
                                    _ = _emailSender.SendEmailWithFIle(bytes, user_Email, "Tamarran - Automatic Payment Confirmation", bodyemail, attachmentTitle);
                                }
                                else
                                {
                                    Invoice invoice = new Invoice
                                    {
                                        InvoiceStartDate = DateTime.UtcNow,
                                        InvoiceEndDate = DateTime.UtcNow.AddMonths(6),
                                        AddedDate = DateTime.UtcNow,
                                        VAT = Vat.ToString(),
                                        Discount = Discount.ToString(),
                                        AddedBy = getuserinfo.FullName,
                                        SubscriptionAmount = Convert.ToDouble(decimal.Round(after_vat_totalamount, 2)),
                                        Currency = getsubinfo.Currency,
                                        SubscriptionId = getsubinfo.SubscriptionId,
                                        Status = "Un-Paid",
                                        IsDeleted = false,
                                        Description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")",
                                        SubscriptionName = getsubinfo.Name,
                                        UserId = getuserinfo.Id,
                                        ChargeId = deserialized_CreateCharge.id,
                                        GymName = getuserinfo.GYMName,
                                        Country = getsubinfo.Countries
                                    };
                                    _context.invoices.Add(invoice);
                                    _context.SaveChanges();
                                    int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
                                    var nameinvoice = "Inv" + max_invoice_id;
                                    var bodyemail = EmailBodyFill.EmailBodyForSubscriptionrenewalinTamarranfailed(getsubinfo, getuserinfo, nameinvoice, Constants.RedirectURL);
                                    _ = _emailSender.SendEmailAsync(user_Email, "Tamarran - Your subscription renewal in Tamarran failed", bodyemail);
                                }
                            }
                            else if (getuserinfo.Frequency == "YEARLY")
                            {
                                // Create a charge
                                var client = new HttpClient();
                                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tap.company/v2/charges");
                                request.Headers.Add("Authorization", "Bearer " + getuserinfo.SecertKey);
                                request.Headers.Add("accept", "application/json");
                                var content = new StringContent("\r\n{\r\n  \"amount\": " + Decimal.Round(after_vat_totalamount, 2) + ",\r\n  \"currency\": \"" + getsubinfo.Currency + "\",\r\n  \"customer_initiated\": false,\r\n  \"threeDSecure\": true,\r\n  \"save_card\": false,\r\n  \"payment_agreement\": {\r\n    \"contract\": {\r\n      \"id\": \"" + getuserinfo.Tap_Card_ID + "\"\r\n    },\r\n    \"id\": \"" + getuserinfo.Tap_Agreement_ID + "\"\r\n  },\r\n  \"receipt\": {\r\n    \"email\": true,\r\n    \"sms\": true\r\n  },\"reference\": {\r\n    \"transaction\": \"" + TransNo + "\",\r\n    \"order\": \"" + OrderNo + "\"\r\n  },\r\n  \"customer\": {\r\n    \"id\": \"" + getuserinfo.Tap_CustomerID + "\"\r\n  },\r\n  \"source\": {\r\n    \"id\": \"" + Deserialized_savecard.id + "\"\r\n  },\r\n  \"redirect\": {\r\n    \"url\": \"https://1f3b186efe31e8696c144578816c5443.m.pipedream.net/\"\r\n  }\r\n}\r\n", null, "application/json");
                                request.Content = content;
                                var response = await client.SendAsync(request);
                                var bodys = await response.Content.ReadAsStringAsync();
                                deserialized_CreateCharge = JsonConvert.DeserializeObject<CreateCharge>(bodys);
                                if (deserialized_CreateCharge.status == "CAPTURED")
                                {
                                    Invoice invoice = new Invoice
                                    {
                                        InvoiceStartDate = DateTime.UtcNow,
                                        InvoiceEndDate = DateTime.UtcNow.AddMonths(12),
                                        AddedDate = DateTime.UtcNow,
                                        AddedBy = getuserinfo.FullName,
                                        SubscriptionAmount = Convert.ToDouble(decimal.Round(after_vat_totalamount, 2)),
                                        VAT = Vat.ToString(),
                                        Discount = Discount.ToString(),
                                        SubscriptionId = getsubinfo.SubscriptionId,
                                        Status = "Payment Captured",
                                        IsDeleted = false,
                                        Currency = getsubinfo.Currency,
                                        Description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")",
                                        SubscriptionName = getsubinfo.Name,
                                        UserId = getuserinfo.Id,
                                        ChargeId = deserialized_CreateCharge.id,
                                        GymName = getuserinfo.GYMName,
                                        Country = getsubinfo.Countries
                                    };
                                    _context.invoices.Add(invoice);
                                    _context.SaveChanges();
                                    int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
                                    //Next Recurrening Job Date
                                    RecurringCharge recurringCharge = new RecurringCharge();
                                    recurringCharge.Amount = Convert.ToDecimal(item.Amount);
                                    recurringCharge.SubscriptionId = getsubinfo.SubscriptionId;
                                    recurringCharge.UserID = getuserinfo.Id;
                                    recurringCharge.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                    recurringCharge.ChargeId = deserialized_CreateCharge.id;
                                    recurringCharge.JobRunDate = invoice.InvoiceEndDate.AddDays(1);
                                    recurringCharge.Invoice = "Inv" + max_invoice_id;
                                    _context.recurringCharges.Add(recurringCharge);
                                    _context.SaveChanges();

                                    // Update Job Table
                                    var recurreningjob = _context.recurringCharges.Where(x => x.RecurringChargeId == item.RecurringChargeId).FirstOrDefault();
                                    recurreningjob.IsRun = true;
                                    recurreningjob.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                    _context.recurringCharges.Update(recurreningjob);
                                    _context.SaveChanges();
                                    //Send Email
                                    var incoice_info = _context.invoices.Where(x => x.InvoiceId == max_invoice_id).FirstOrDefault();
                                    string body = string.Empty;
                                    _environment.WebRootPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                                    string contentRootPath = _environment.WebRootPath + "/htmltopdfRecurrening.html";
                                    string contentRootPath1 = _environment.WebRootPath + "/css/bootstrap.min.css";
                                    //Generate PDF
                                    using (StreamReader reader = new StreamReader(contentRootPath))
                                    {
                                        body = reader.ReadToEnd();
                                    }
                                    //Fill EMail By Parameter
                                    body = body.Replace("{title}", "Tamarran Payment Invoice");
                                    body = body.Replace("{currentdate}", DateTime.UtcNow.ToString("dd-MM-yyyy"));

                                    body = body.Replace("{InvocieStatus}", "Payment Captured");
                                    body = body.Replace("{InvoiceID}", "Inv" + max_invoice_id);


                                    body = body.Replace("{User_Name}", getuserinfo.FullName);
                                    body = body.Replace("{User_Email}", user_Email);
                                    body = body.Replace("{User_GYM}", getuserinfo.GYMName);
                                    body = body.Replace("{User_Phone}", getuserinfo.PhoneNumber);

                                    body = body.Replace("{SubscriptionName}", getsubinfo.Name);
                                    body = body.Replace("{Discount}", Discount.ToString());
                                    body = body.Replace("{SubscriptionPeriod}", getuserinfo.Frequency);
                                    body = body.Replace("{SetupFee}", "0.0" + " " + getsubinfo.Currency);
                                    int amount = Convert.ToInt32(incoice_info.SubscriptionAmount);
                                    body = body.Replace("{SubscriptionAmount}", decimal.Round(sun_amount, 2).ToString() + " " + getsubinfo.Currency);
                                    //Calculate VAT
                                    if (getsubinfo.VAT == null || getsubinfo.VAT == "0")
                                    {
                                        body = body.Replace("{VAT}", "0.00");
                                        body = body.Replace("{Total}", amount.ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{InvoiceAmount}", amount.ToString() + " " + getsubinfo.Currency);
                                        var without_vat = Convert.ToDecimal(finalamount);
                                        body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(without_vat), 2).ToString() + " " + getsubinfo.Currency);
                                    }
                                    else
                                    {
                                        body = body.Replace("{VAT}", decimal.Round(Convert.ToDecimal(Vat), 2).ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{Total}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{InvoiceAmount}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                        var without_vat = Convert.ToDecimal(finalamount);
                                        body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(without_vat), 2).ToString() + " " + getsubinfo.Currency);
                                    }
                                    var bytes = (new NReco.PdfGenerator.HtmlToPdfConverter()).GeneratePdf(body);
                                    var bodyemail = EmailBodyFill.EmailBodyForAutomaticPaymentConfirmation(getsubinfo, getuserinfo);
                                    _ = _emailSender.SendEmailWithFIle(bytes, user_Email, "Tamarran - Automatic Payment Confirmation", bodyemail, attachmentTitle);
                                }
                                else
                                {
                                    Invoice invoice = new Invoice
                                    {
                                        InvoiceStartDate = DateTime.UtcNow,
                                        InvoiceEndDate = DateTime.UtcNow.AddMonths(12),
                                        AddedDate = DateTime.UtcNow,
                                        AddedBy = getuserinfo.FullName,
                                        SubscriptionAmount = Convert.ToDouble(decimal.Round(after_vat_totalamount, 2)),
                                        VAT = Vat.ToString(),
                                        Discount = Discount.ToString(),
                                        SubscriptionId = getsubinfo.SubscriptionId,
                                        Status = "Un-Paid",
                                        IsDeleted = false,
                                        Currency = getsubinfo.Currency,
                                        Description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")",
                                        SubscriptionName = getsubinfo.Name,
                                        UserId = getuserinfo.Id,
                                        ChargeId = deserialized_CreateCharge.id,
                                        GymName = getuserinfo.GYMName,
                                        Country = getsubinfo.Countries
                                    };
                                    _context.invoices.Add(invoice);
                                    _context.SaveChanges();
                                    int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
                                    var nameinvoice = "Inv" + max_invoice_id;
                                    var bodyemail = EmailBodyFill.EmailBodyForSubscriptionrenewalinTamarranfailed(getsubinfo, getuserinfo, nameinvoice, Constants.RedirectURL);
                                    _ = _emailSender.SendEmailAsync(user_Email, "Tamarran - Your subscription renewal in Tamarran failed", bodyemail);
                                }
                            }
                        }
                    }
                }
            }

        }
        public async Task AutoChargeJobForBenefit()
        {
            CreateCharge deserialized_CreateCharge = null;
            var recurringCharges_list = _context.recurringCharges.Where(x => x.JobRunDate.Date == DateTime.UtcNow.Date && x.IsRun == false).ToList();
            //var recurringCharges_list = _context.recurringCharges.Where(x => x.IsRun == false).ToList();
            foreach (var item in recurringCharges_list)
            {
                string[] result = item.ChargeId.Split('_').ToArray();
                if (result[0] != "chg")
                {
                    var getsubinfo = _context.subscriptions.Where(x => x.SubscriptionId == item.SubscriptionId).FirstOrDefault();
                    var getuserinfo = _context.Users.Where(x => x.Id == item.UserID).FirstOrDefault();
                    if (getuserinfo != null && getuserinfo.Country == "Bahrain")
                    {
                        if (getuserinfo.SubscribeID > 0 && getuserinfo.Status == true)
                        {
                            string user_Email = getuserinfo.Email;
                            string attachmentTitle = $"{getuserinfo.FullName}_Invoice_Details";

                            if (getuserinfo.Frequency == "DAILY")
                            {
                                Random rnd = new Random();
                                var TransNo = "Txn_" + rnd.Next(10000000, 99999999);
                                var OrderNo = "Ord_" + rnd.Next(10000000, 99999999);
                                //var amount = decimal.Round(Convert.ToDecimal(item.Amount));
                                var description = getsubinfo.Frequency;
                                Reference reference = new Reference();
                                reference.transaction = TransNo;
                                reference.order = OrderNo;

                                long ExpireLink = new DateTimeOffset(DateTime.UtcNow.AddYears(1)).ToUnixTimeMilliseconds();
                                long Due = 0;
                                int days = DateTime.DaysInMonth(DateTime.UtcNow.Year, DateTime.UtcNow.Month);
                                decimal finalamount = 0;
                                decimal Discount = 0;
                                decimal Vat = 0;
                                decimal sun_amount = 0;

                                if (getuserinfo.Frequency == "DAILY")
                                {
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddDays(2)).ToUnixTimeMilliseconds();
                                    Discount = 0;
                                    finalamount = (decimal)Convert.ToInt32(getsubinfo.Amount) / (int)days;
                                }
                                else if (getuserinfo.Frequency == "WEEKLY")
                                {
                                    Discount = 0;
                                    finalamount = (decimal)Convert.ToInt32(getsubinfo.Amount) / 4;
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddDays(8)).ToUnixTimeMilliseconds();
                                }
                                else if (getuserinfo.Frequency == "MONTHLY")
                                {
                                    Discount = 0;
                                    finalamount = (decimal)Convert.ToInt32(getsubinfo.Amount);
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddMonths(1).AddDays(1)).ToUnixTimeMilliseconds();
                                }
                                else if (getuserinfo.Frequency == "QUARTERLY")
                                {
                                    Discount = 0;
                                    finalamount = (decimal)(Convert.ToInt32(getsubinfo.Amount) * 3) / 1;
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddMonths(3).AddDays(1)).ToUnixTimeMilliseconds();
                                }
                                else if (getuserinfo.Frequency == "HALFYEARLY")
                                {
                                    Discount = 0;
                                    finalamount = (decimal)(Convert.ToInt32(getsubinfo.Amount) * 6) / 1;
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddMonths(6).AddDays(1)).ToUnixTimeMilliseconds();
                                }
                                else if (getuserinfo.Frequency == "YEARLY")
                                {
                                    var amountpercentage = (decimal)(Convert.ToInt32(getsubinfo.Amount) / 100) * Convert.ToDecimal(getsubinfo.Discount);
                                    var final_amount_percentage = Convert.ToInt32(getsubinfo.Amount) - amountpercentage;
                                    finalamount = final_amount_percentage * 12;
                                    Discount = amountpercentage * 12;
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddYears(1).AddDays(1)).ToUnixTimeMilliseconds();
                                }
                                if (getsubinfo.VAT == null || getsubinfo.VAT == "0")
                                {
                                    Vat = 0;
                                }
                                else
                                {
                                    decimal totala = finalamount;// + Convert.ToDecimal(getsubinfo.SetupFee);
                                    sun_amount = totala;
                                    Vat = (decimal)((totala / Convert.ToInt32(getsubinfo.VAT)) * 100) / 100;
                                }
                                //decimal after_vat_totalamount = finalamount + Convert.ToDecimal(getsubinfo.SetupFee) + Vat;
                                decimal after_vat_totalamount = finalamount + Vat;

                                Redirect redirect = new Redirect();
                                redirect.url = Constants.RedirectURL + "/Home/CardVerifyBenefit";

                                Post post = new Post();
                                post.url = Constants.RedirectURL + "/Home/CardVerifyBenefits";

                                var countrycode = "";
                                if (getuserinfo.Country == "Bahrain")
                                {
                                    countrycode = "+973";
                                }
                                else if (getuserinfo.Country == "KSA")
                                {
                                    countrycode = "+966";
                                }
                                else if (getuserinfo.Country == "Kuwait")
                                {
                                    countrycode = "+965";
                                }
                                else if (getuserinfo.Country == "UAE")
                                {
                                    countrycode = "+971";
                                }
                                else if (getuserinfo.Country == "Qatar")
                                {
                                    countrycode = "+974";
                                }
                                else if (getuserinfo.Country == "Oman")
                                {
                                    countrycode = "+968";
                                }
                                var currency = getsubinfo.Currency;
                                Phone phone = new Phone();
                                phone.number = getuserinfo.PhoneNumber;
                                phone.country_code = countrycode;

                                Customer customer = new Customer();
                                customer.id = getuserinfo.Tap_CustomerID;

                                Receipt receipt = new Receipt();
                                receipt.sms = true;
                                receipt.email = true;

                                Notifications notifications = new Notifications();
                                List<string> receipts = new List<string>();
                                receipts.Add("SMS");
                                receipts.Add("EMAIL");
                                notifications.channels = receipts;
                                notifications.dispatch = true;

                                List<string> currencies = new List<string>();
                                currencies.Add(getsubinfo.Currency);

                                Charge charge = new Charge();
                                charge.receipt = receipt;
                                charge.statement_descriptor = "test";

                                List<string> p_methods = new List<string>();
                                p_methods.Add("BENEFIT");

                                List<Item> items = new List<Item>();
                                Item itemss = new Item();
                                itemss.image = "";
                                itemss.quantity = 1;
                                itemss.name = "Invoice Amount";
                                itemss.amount = Math.Round(after_vat_totalamount, 2).ToString("0.00");
                                itemss.currency = getsubinfo.Currency;
                                items.Add(itemss);

                                Order order = new Order();
                                order.amount = Math.Round(after_vat_totalamount, 2).ToString("0.00");
                                order.currency = getsubinfo.Currency;
                                order.items = items;


                                TapInvoice tapInvoice = new TapInvoice();
                                tapInvoice.redirect = redirect;
                                tapInvoice.post = post;
                                tapInvoice.customer = customer;
                                tapInvoice.draft = false;
                                tapInvoice.due = Due;
                                tapInvoice.expiry = ExpireLink;
                                tapInvoice.description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")";
                                tapInvoice.mode = "INVOICE";
                                tapInvoice.note = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")";
                                tapInvoice.notifications = notifications;
                                tapInvoice.currencies = currencies;
                                tapInvoice.charge = charge;
                                tapInvoice.payment_methods = p_methods;
                                tapInvoice.reference = reference;
                                tapInvoice.order = order;


                                var jsonmodel = JsonConvert.SerializeObject(tapInvoice);
                                var client = new HttpClient();
                                var request = new HttpRequestMessage
                                {
                                    Method = HttpMethod.Post,
                                    RequestUri = new Uri("https://api.tap.company/v2/invoices/"),
                                    Headers =
                                      {
                                          { "accept", "application/json" },
                                          { "Authorization", "Bearer " + getuserinfo.SecertKey },
                                      },
                                    Content = new StringContent(jsonmodel)
                                    {
                                        Headers =
                                      {
                                          ContentType = new MediaTypeHeaderValue("application/json")
                                      }
                                    }
                                };
                                TapInvoiceResponse myDeserializedClass = null;
                                using (var response = await client.SendAsync(request))
                                {
                                    var bodys = await response.Content.ReadAsStringAsync();
                                    myDeserializedClass = JsonConvert.DeserializeObject<TapInvoiceResponse>(bodys);
                                }
                                if (myDeserializedClass.status == "CREATED")
                                {
                                    Invoice invoice = new Invoice
                                    {
                                        InvoiceStartDate = DateTime.UtcNow,
                                        InvoiceEndDate = DateTime.UtcNow.AddDays(1),
                                        Currency = getsubinfo.Currency,
                                        AddedDate = DateTime.UtcNow,
                                        AddedBy = getuserinfo.FullName,
                                        SubscriptionAmount = Convert.ToDouble(decimal.Round(after_vat_totalamount, 2)),
                                        SubscriptionId = Convert.ToInt32(getsubinfo.SubscriptionId),
                                        Status = "Un-Paid",
                                        IsDeleted = false,
                                        VAT = Vat.ToString(),
                                        Discount = Discount.ToString(),
                                        Description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")",
                                        SubscriptionName = getsubinfo.Name,
                                        UserId = getuserinfo.Id,
                                        ChargeId = myDeserializedClass.id,
                                        GymName = getuserinfo.GYMName,
                                        Country = getsubinfo.Countries
                                    };
                                    _context.invoices.Add(invoice);
                                    _context.SaveChanges();
                                    int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
                                    //Next Recurrening Job Date
                                    RecurringCharge recurringCharge = new RecurringCharge();
                                    recurringCharge.Amount = Convert.ToDecimal(item.Amount);
                                    recurringCharge.SubscriptionId = getsubinfo.SubscriptionId;
                                    recurringCharge.UserID = getuserinfo.Id;
                                    recurringCharge.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                    recurringCharge.ChargeId = myDeserializedClass.id;
                                    recurringCharge.JobRunDate = invoice.InvoiceEndDate;
                                    recurringCharge.Invoice = "Inv" + max_invoice_id;
                                    _context.recurringCharges.Add(recurringCharge);
                                    _context.SaveChanges();


                                    // Update Job Table
                                    var recurreningjob = _context.recurringCharges.Where(x => x.RecurringChargeId == item.RecurringChargeId).FirstOrDefault();
                                    recurreningjob.IsRun = true;
                                    recurreningjob.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                    _context.recurringCharges.Update(recurreningjob);
                                    _context.SaveChanges();
                                    //Send Email
                                    var incoice_info = _context.invoices.Where(x => x.InvoiceId == max_invoice_id).FirstOrDefault();
                                    string body = string.Empty;
                                    _environment.WebRootPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                                    string contentRootPath = _environment.WebRootPath + "/htmltopdfP.html";
                                    string contentRootPath1 = _environment.WebRootPath + "/css/bootstrap.min.css";
                                    //Generate PDF
                                    using (StreamReader reader = new StreamReader(contentRootPath))
                                    {
                                        body = reader.ReadToEnd();
                                    }
                                    //Fill EMail By Parameter
                                    body = body.Replace("{title}", "Tamarran Payment Invoice");
                                    body = body.Replace("{currentdate}", DateTime.UtcNow.ToString("dd-MM-yyyy"));

                                    body = body.Replace("{InvocieStatus}", "Unpaid");
                                    body = body.Replace("{InvoiceID}", "Inv" + max_invoice_id);


                                    body = body.Replace("{User_Name}", getuserinfo.FullName);
                                    body = body.Replace("{User_Email}", user_Email);
                                    body = body.Replace("{User_GYM}", getuserinfo.GYMName);
                                    body = body.Replace("{User_Phone}", getuserinfo.PhoneNumber);

                                    body = body.Replace("{SubscriptionName}", getsubinfo.Name);
                                    body = body.Replace("{Discount}", Discount.ToString());
                                    body = body.Replace("{SubscriptionPeriod}", getuserinfo.Frequency);
                                    body = body.Replace("{SetupFee}", "0.0" + " " + getsubinfo.Currency);
                                    int amount = Convert.ToInt32(incoice_info.SubscriptionAmount);
                                    body = body.Replace("{SubscriptionAmount}", decimal.Round(sun_amount, 2).ToString() + " " + getsubinfo.Currency);
                                    //Calculate VAT
                                    if (getsubinfo.VAT == null || getsubinfo.VAT == "0")
                                    {
                                        body = body.Replace("{VAT}", "0.00");
                                        body = body.Replace("{Total}", amount.ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{InvoiceAmount}", amount.ToString() + " " + getsubinfo.Currency);
                                        var without_vat = Convert.ToDecimal(finalamount);
                                        body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(without_vat), 2).ToString() + " " + getsubinfo.Currency);
                                    }
                                    else
                                    {
                                        body = body.Replace("{VAT}", decimal.Round(Convert.ToDecimal(Vat), 2).ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{Total}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{InvoiceAmount}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                        var without_vat = Convert.ToDecimal(finalamount);
                                        body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(without_vat), 2).ToString() + " " + getsubinfo.Currency);
                                    }
                                    var bytes = (new NReco.PdfGenerator.HtmlToPdfConverter()).GeneratePdf(body);
                                    var bodyemail = EmailBodyFill.EmailBodyForBenefitPaymentRequest(getsubinfo, getuserinfo, myDeserializedClass.url);

                                    _ = _emailSender.SendEmailWithFIle(bytes, user_Email, "Tamarran - Automatic Payment Request", bodyemail, attachmentTitle);
                                }
                            }
                            else if (getuserinfo.Frequency == "WEEKLY")
                            {
                                Random rnd = new Random();
                                var TransNo = "Txn_" + rnd.Next(10000000, 99999999);
                                var OrderNo = "Ord_" + rnd.Next(10000000, 99999999);
                                //var amount = decimal.Round(Convert.ToDecimal(item.Amount));
                                var description = getsubinfo.Frequency;
                                Reference reference = new Reference();
                                reference.transaction = TransNo;
                                reference.order = OrderNo;

                                long ExpireLink = new DateTimeOffset(DateTime.UtcNow.AddYears(1)).ToUnixTimeMilliseconds();
                                long Due = 0;
                                int days = DateTime.DaysInMonth(DateTime.UtcNow.Year, DateTime.UtcNow.Month);
                                decimal finalamount = 0;
                                decimal Discount = 0;
                                decimal Vat = 0;
                                decimal sun_amount = 0;

                                if (getuserinfo.Frequency == "DAILY")
                                {
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddDays(2)).ToUnixTimeMilliseconds();
                                    Discount = 0;
                                    finalamount = (decimal)Convert.ToInt32(getsubinfo.Amount) / (int)days;
                                }
                                else if (getuserinfo.Frequency == "WEEKLY")
                                {
                                    Discount = 0;
                                    finalamount = (decimal)Convert.ToInt32(getsubinfo.Amount) / 4;
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddDays(8)).ToUnixTimeMilliseconds();
                                }
                                else if (getuserinfo.Frequency == "MONTHLY")
                                {
                                    Discount = 0;
                                    finalamount = (decimal)Convert.ToInt32(getsubinfo.Amount);
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddMonths(1).AddDays(1)).ToUnixTimeMilliseconds();
                                }
                                else if (getuserinfo.Frequency == "QUARTERLY")
                                {
                                    Discount = 0;
                                    finalamount = (decimal)(Convert.ToInt32(getsubinfo.Amount) * 3) / 1;
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddMonths(3).AddDays(1)).ToUnixTimeMilliseconds();
                                }
                                else if (getuserinfo.Frequency == "HALFYEARLY")
                                {
                                    Discount = 0;
                                    finalamount = (decimal)(Convert.ToInt32(getsubinfo.Amount) * 6) / 1;
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddMonths(6).AddDays(1)).ToUnixTimeMilliseconds();
                                }
                                else if (getuserinfo.Frequency == "YEARLY")
                                {
                                    var amountpercentage = (decimal)(Convert.ToInt32(getsubinfo.Amount) / 100) * Convert.ToDecimal(getsubinfo.Discount);
                                    var final_amount_percentage = Convert.ToInt32(getsubinfo.Amount) - amountpercentage;
                                    finalamount = final_amount_percentage * 12;
                                    Discount = amountpercentage * 12;
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddYears(1).AddDays(1)).ToUnixTimeMilliseconds();
                                }
                                if (getsubinfo.VAT == null || getsubinfo.VAT == "0")
                                {
                                    Vat = 0;
                                }
                                else
                                {
                                    decimal totala = finalamount + Convert.ToDecimal(getsubinfo.SetupFee);
                                    sun_amount = totala;
                                    Vat = (decimal)((totala / Convert.ToInt32(getsubinfo.VAT)) * 100) / 100;
                                }
                                decimal after_vat_totalamount = finalamount + Convert.ToDecimal(getsubinfo.SetupFee) + Vat;

                                Redirect redirect = new Redirect();
                                redirect.url = Constants.RedirectURL + "/Home/CardVerifyBenefit";

                                Post post = new Post();
                                post.url = Constants.RedirectURL + "/Home/CardVerifyBenefits";

                                var countrycode = "";
                                if (getuserinfo.Country == "Bahrain")
                                {
                                    countrycode = "+973";
                                }
                                else if (getuserinfo.Country == "KSA")
                                {
                                    countrycode = "+966";
                                }
                                else if (getuserinfo.Country == "Kuwait")
                                {
                                    countrycode = "+965";
                                }
                                else if (getuserinfo.Country == "UAE")
                                {
                                    countrycode = "+971";
                                }
                                else if (getuserinfo.Country == "Qatar")
                                {
                                    countrycode = "+974";
                                }
                                else if (getuserinfo.Country == "Oman")
                                {
                                    countrycode = "+968";
                                }
                                var currency = getsubinfo.Currency;
                                Phone phone = new Phone();
                                phone.number = getuserinfo.PhoneNumber;
                                phone.country_code = countrycode;

                                Customer customer = new Customer();
                                customer.id = getuserinfo.Tap_CustomerID;

                                Receipt receipt = new Receipt();
                                receipt.sms = true;
                                receipt.email = true;

                                Notifications notifications = new Notifications();
                                List<string> receipts = new List<string>();
                                receipts.Add("SMS");
                                receipts.Add("EMAIL");
                                notifications.channels = receipts;
                                notifications.dispatch = true;

                                List<string> currencies = new List<string>();
                                currencies.Add(getsubinfo.Currency);

                                Charge charge = new Charge();
                                charge.receipt = receipt;
                                charge.statement_descriptor = "test";

                                List<string> p_methods = new List<string>();
                                p_methods.Add("BENEFIT");

                                List<Item> items = new List<Item>();
                                Item itemss = new Item();
                                itemss.image = "";
                                itemss.quantity = 1;
                                itemss.name = "Invoice Amount";
                                itemss.amount = Math.Round(after_vat_totalamount, 2).ToString("0.00");
                                itemss.currency = getsubinfo.Currency;
                                items.Add(itemss);

                                Order order = new Order();
                                order.amount = Math.Round(after_vat_totalamount, 2).ToString("0.00");
                                order.currency = getsubinfo.Currency;
                                order.items = items;


                                TapInvoice tapInvoice = new TapInvoice();
                                tapInvoice.redirect = redirect;
                                tapInvoice.post = post;
                                tapInvoice.customer = customer;
                                tapInvoice.draft = false;
                                tapInvoice.due = Due;
                                tapInvoice.expiry = ExpireLink;
                                tapInvoice.description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")";
                                tapInvoice.mode = "INVOICE";
                                tapInvoice.note = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")";
                                tapInvoice.notifications = notifications;
                                tapInvoice.currencies = currencies;
                                tapInvoice.charge = charge;
                                tapInvoice.payment_methods = p_methods;
                                tapInvoice.reference = reference;
                                tapInvoice.order = order;


                                var jsonmodel = JsonConvert.SerializeObject(tapInvoice);
                                var client = new HttpClient();
                                var request = new HttpRequestMessage
                                {
                                    Method = HttpMethod.Post,
                                    RequestUri = new Uri("https://api.tap.company/v2/invoices/"),
                                    Headers =
                                      {
                                          { "accept", "application/json" },
                                          { "Authorization", "Bearer " + getuserinfo.SecertKey },
                                      },
                                    Content = new StringContent(jsonmodel)
                                    {
                                        Headers =
                                      {
                                          ContentType = new MediaTypeHeaderValue("application/json")
                                      }
                                    }
                                };
                                TapInvoiceResponse myDeserializedClass = null;
                                using (var response = await client.SendAsync(request))
                                {
                                    var bodys = await response.Content.ReadAsStringAsync();
                                    myDeserializedClass = JsonConvert.DeserializeObject<TapInvoiceResponse>(bodys);
                                }
                                if (myDeserializedClass.status == "CREATED")
                                {
                                    Invoice invoice = new Invoice
                                    {
                                        InvoiceStartDate = DateTime.UtcNow,
                                        InvoiceEndDate = DateTime.UtcNow.AddDays(7),
                                        Currency = getsubinfo.Currency,
                                        AddedDate = DateTime.UtcNow,
                                        AddedBy = getuserinfo.FullName,
                                        SubscriptionAmount = Convert.ToDouble(decimal.Round(after_vat_totalamount, 2)),
                                        SubscriptionId = Convert.ToInt32(getsubinfo.SubscriptionId),
                                        Status = "Un-Paid",
                                        IsDeleted = false,
                                        VAT = Vat.ToString(),
                                        Discount = Discount.ToString(),
                                        Description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")",
                                        SubscriptionName = getsubinfo.Name,
                                        UserId = getuserinfo.Id,
                                        ChargeId = myDeserializedClass.id,
                                        GymName = getuserinfo.GYMName,
                                        Country = getsubinfo.Countries
                                    };
                                    _context.invoices.Add(invoice);
                                    _context.SaveChanges();
                                    int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
                                    //Next Recurrening Job Date
                                    RecurringCharge recurringCharge = new RecurringCharge();
                                    recurringCharge.Amount = Convert.ToDecimal(item.Amount);
                                    recurringCharge.SubscriptionId = getsubinfo.SubscriptionId;
                                    recurringCharge.UserID = getuserinfo.Id;
                                    recurringCharge.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                    recurringCharge.ChargeId = myDeserializedClass.id;
                                    recurringCharge.JobRunDate = invoice.InvoiceEndDate.AddDays(1);
                                    recurringCharge.Invoice = "Inv" + max_invoice_id;
                                    _context.recurringCharges.Add(recurringCharge);
                                    _context.SaveChanges();


                                    // Update Job Table
                                    var recurreningjob = _context.recurringCharges.Where(x => x.RecurringChargeId == item.RecurringChargeId).FirstOrDefault();
                                    recurreningjob.IsRun = true;
                                    recurreningjob.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                    _context.recurringCharges.Update(recurreningjob);
                                    _context.SaveChanges();
                                    //Send Email
                                    var incoice_info = _context.invoices.Where(x => x.InvoiceId == max_invoice_id).FirstOrDefault();
                                    string body = string.Empty;
                                    _environment.WebRootPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                                    string contentRootPath = _environment.WebRootPath + "/htmltopdfP.html";
                                    string contentRootPath1 = _environment.WebRootPath + "/css/bootstrap.min.css";
                                    //Generate PDF
                                    using (StreamReader reader = new StreamReader(contentRootPath))
                                    {
                                        body = reader.ReadToEnd();
                                    }
                                    //Fill EMail By Parameter
                                    body = body.Replace("{title}", "Tamarran Payment Invoice");
                                    body = body.Replace("{currentdate}", DateTime.UtcNow.ToString("dd-MM-yyyy"));

                                    body = body.Replace("{InvocieStatus}", "Unpaid");
                                    body = body.Replace("{InvoiceID}", "Inv" + max_invoice_id);


                                    body = body.Replace("{User_Name}", getuserinfo.FullName);
                                    body = body.Replace("{User_Email}", user_Email);
                                    body = body.Replace("{User_GYM}", getuserinfo.GYMName);
                                    body = body.Replace("{User_Phone}", getuserinfo.PhoneNumber);

                                    body = body.Replace("{SubscriptionName}", getsubinfo.Name);
                                    body = body.Replace("{Discount}", Discount.ToString());
                                    body = body.Replace("{SubscriptionPeriod}", getuserinfo.Frequency);
                                    body = body.Replace("{SetupFee}", "0.0" + " " + getsubinfo.Currency);
                                    int amount = Convert.ToInt32(incoice_info.SubscriptionAmount);
                                    body = body.Replace("{SubscriptionAmount}", decimal.Round(sun_amount, 2).ToString() + " " + getsubinfo.Currency);
                                    //Calculate VAT
                                    if (getsubinfo.VAT == null || getsubinfo.VAT == "0")
                                    {
                                        body = body.Replace("{VAT}", "0.00");
                                        body = body.Replace("{Total}", amount.ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{InvoiceAmount}", amount.ToString() + " " + getsubinfo.Currency);
                                        var without_vat = Convert.ToDecimal(finalamount);
                                        body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(without_vat), 2).ToString() + " " + getsubinfo.Currency);
                                    }
                                    else
                                    {
                                        body = body.Replace("{VAT}", decimal.Round(Convert.ToDecimal(Vat), 2).ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{Total}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{InvoiceAmount}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                        var without_vat = Convert.ToDecimal(finalamount);
                                        body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(without_vat), 2).ToString() + " " + getsubinfo.Currency);
                                    }
                                    var bytes = (new NReco.PdfGenerator.HtmlToPdfConverter()).GeneratePdf(body);
                                    var bodyemail = EmailBodyFill.EmailBodyForBenefitPaymentRequest(getsubinfo, getuserinfo, myDeserializedClass.url);
                                    _ = _emailSender.SendEmailWithFIle(bytes, user_Email, "Tamarran - Automatic Payment Request", bodyemail, attachmentTitle);
                                }

                            }
                            else if (getuserinfo.Frequency == "MONTHLY")
                            {
                                Random rnd = new Random();
                                var TransNo = "Txn_" + rnd.Next(10000000, 99999999);
                                var OrderNo = "Ord_" + rnd.Next(10000000, 99999999);
                                //var amount = decimal.Round(Convert.ToDecimal(item.Amount));
                                var description = getsubinfo.Frequency;
                                Reference reference = new Reference();
                                reference.transaction = TransNo;
                                reference.order = OrderNo;

                                long ExpireLink = new DateTimeOffset(DateTime.UtcNow.AddYears(1)).ToUnixTimeMilliseconds();
                                long Due = 0;
                                int days = DateTime.DaysInMonth(DateTime.UtcNow.Year, DateTime.UtcNow.Month);
                                decimal finalamount = 0;
                                decimal Discount = 0;
                                decimal Vat = 0;
                                decimal sun_amount = 0;

                                if (getuserinfo.Frequency == "DAILY")
                                {
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddDays(2)).ToUnixTimeMilliseconds();
                                    Discount = 0;
                                    finalamount = (decimal)Convert.ToInt32(getsubinfo.Amount) / (int)days;
                                }
                                else if (getuserinfo.Frequency == "WEEKLY")
                                {
                                    Discount = 0;
                                    finalamount = (decimal)Convert.ToInt32(getsubinfo.Amount) / 4;
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddDays(8)).ToUnixTimeMilliseconds();
                                }
                                else if (getuserinfo.Frequency == "MONTHLY")
                                {
                                    Discount = 0;
                                    finalamount = (decimal)Convert.ToInt32(getsubinfo.Amount);
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddMonths(1).AddDays(1)).ToUnixTimeMilliseconds();
                                }
                                else if (getuserinfo.Frequency == "QUARTERLY")
                                {
                                    Discount = 0;
                                    finalamount = (decimal)(Convert.ToInt32(getsubinfo.Amount) * 3) / 1;
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddMonths(3).AddDays(1)).ToUnixTimeMilliseconds();
                                }
                                else if (getuserinfo.Frequency == "HALFYEARLY")
                                {
                                    Discount = 0;
                                    finalamount = (decimal)(Convert.ToInt32(getsubinfo.Amount) * 6) / 1;
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddMonths(6).AddDays(1)).ToUnixTimeMilliseconds();
                                }
                                else if (getuserinfo.Frequency == "YEARLY")
                                {
                                    var amountpercentage = (decimal)(Convert.ToInt32(getsubinfo.Amount) / 100) * Convert.ToDecimal(getsubinfo.Discount);
                                    var final_amount_percentage = Convert.ToInt32(getsubinfo.Amount) - amountpercentage;
                                    finalamount = final_amount_percentage * 12;
                                    Discount = amountpercentage * 12;
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddYears(1).AddDays(1)).ToUnixTimeMilliseconds();
                                }
                                if (getsubinfo.VAT == null || getsubinfo.VAT == "0")
                                {
                                    Vat = 0;
                                }
                                else
                                {
                                    decimal totala = finalamount + Convert.ToDecimal(getsubinfo.SetupFee);
                                    sun_amount = totala;
                                    Vat = (decimal)((totala / Convert.ToInt32(getsubinfo.VAT)) * 100) / 100;
                                }
                                decimal after_vat_totalamount = finalamount + Convert.ToDecimal(getsubinfo.SetupFee) + Vat;

                                Redirect redirect = new Redirect();
                                redirect.url = Constants.RedirectURL + "/Home/CardVerifyBenefit";

                                Post post = new Post();
                                post.url = Constants.RedirectURL + "/Home/CardVerifyBenefits";

                                var countrycode = "";
                                if (getuserinfo.Country == "Bahrain")
                                {
                                    countrycode = "+973";
                                }
                                else if (getuserinfo.Country == "KSA")
                                {
                                    countrycode = "+966";
                                }
                                else if (getuserinfo.Country == "Kuwait")
                                {
                                    countrycode = "+965";
                                }
                                else if (getuserinfo.Country == "UAE")
                                {
                                    countrycode = "+971";
                                }
                                else if (getuserinfo.Country == "Qatar")
                                {
                                    countrycode = "+974";
                                }
                                else if (getuserinfo.Country == "Oman")
                                {
                                    countrycode = "+968";
                                }
                                var currency = getsubinfo.Currency;
                                Phone phone = new Phone();
                                phone.number = getuserinfo.PhoneNumber;
                                phone.country_code = countrycode;

                                Customer customer = new Customer();
                                customer.id = getuserinfo.Tap_CustomerID;

                                Receipt receipt = new Receipt();
                                receipt.sms = true;
                                receipt.email = true;

                                Notifications notifications = new Notifications();
                                List<string> receipts = new List<string>();
                                receipts.Add("SMS");
                                receipts.Add("EMAIL");
                                notifications.channels = receipts;
                                notifications.dispatch = true;

                                List<string> currencies = new List<string>();
                                currencies.Add(getsubinfo.Currency);

                                Charge charge = new Charge();
                                charge.receipt = receipt;
                                charge.statement_descriptor = "test";

                                List<string> p_methods = new List<string>();
                                p_methods.Add("BENEFIT");

                                List<Item> items = new List<Item>();
                                Item itemss = new Item();
                                itemss.image = "";
                                itemss.quantity = 1;
                                itemss.name = "Invoice Amount";
                                itemss.amount = Math.Round(after_vat_totalamount, 2).ToString("0.00");
                                itemss.currency = getsubinfo.Currency;
                                items.Add(itemss);

                                Order order = new Order();
                                order.amount = Math.Round(after_vat_totalamount, 2).ToString("0.00");
                                order.currency = getsubinfo.Currency;
                                order.items = items;


                                TapInvoice tapInvoice = new TapInvoice();
                                tapInvoice.redirect = redirect;
                                tapInvoice.post = post;
                                tapInvoice.customer = customer;
                                tapInvoice.draft = false;
                                tapInvoice.due = Due;
                                tapInvoice.expiry = ExpireLink;
                                tapInvoice.description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")";
                                tapInvoice.mode = "INVOICE";
                                tapInvoice.note = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")";
                                tapInvoice.notifications = notifications;
                                tapInvoice.currencies = currencies;
                                tapInvoice.charge = charge;
                                tapInvoice.payment_methods = p_methods;
                                tapInvoice.reference = reference;
                                tapInvoice.order = order;


                                var jsonmodel = JsonConvert.SerializeObject(tapInvoice);
                                var client = new HttpClient();
                                var request = new HttpRequestMessage
                                {
                                    Method = HttpMethod.Post,
                                    RequestUri = new Uri("https://api.tap.company/v2/invoices/"),
                                    Headers =
                                      {
                                          { "accept", "application/json" },
                                          { "Authorization", "Bearer " + getuserinfo.SecertKey },
                                      },
                                    Content = new StringContent(jsonmodel)
                                    {
                                        Headers =
                                      {
                                          ContentType = new MediaTypeHeaderValue("application/json")
                                      }
                                    }
                                };
                                TapInvoiceResponse myDeserializedClass = null;
                                using (var response = await client.SendAsync(request))
                                {
                                    var bodys = await response.Content.ReadAsStringAsync();
                                    myDeserializedClass = JsonConvert.DeserializeObject<TapInvoiceResponse>(bodys);
                                }
                                if (myDeserializedClass.status == "CREATED")
                                {
                                    Invoice invoice = new Invoice
                                    {
                                        InvoiceStartDate = DateTime.UtcNow,
                                        InvoiceEndDate = DateTime.UtcNow.AddMonths(1),
                                        Currency = getsubinfo.Currency,
                                        AddedDate = DateTime.UtcNow,
                                        AddedBy = getuserinfo.FullName,
                                        SubscriptionAmount = Convert.ToDouble(decimal.Round(after_vat_totalamount, 2)),
                                        SubscriptionId = Convert.ToInt32(getsubinfo.SubscriptionId),
                                        Status = "Un-Paid",
                                        IsDeleted = false,
                                        VAT = Vat.ToString(),
                                        Discount = Discount.ToString(),
                                        Description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")",
                                        SubscriptionName = getsubinfo.Name,
                                        UserId = getuserinfo.Id,
                                        ChargeId = myDeserializedClass.id,
                                        GymName = getuserinfo.GYMName,
                                        Country = getsubinfo.Countries
                                    };
                                    _context.invoices.Add(invoice);
                                    _context.SaveChanges();
                                    int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
                                    //Next Recurrening Job Date
                                    RecurringCharge recurringCharge = new RecurringCharge();
                                    recurringCharge.Amount = Convert.ToDecimal(item.Amount);
                                    recurringCharge.SubscriptionId = getsubinfo.SubscriptionId;
                                    recurringCharge.UserID = getuserinfo.Id;
                                    recurringCharge.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                    recurringCharge.ChargeId = deserialized_CreateCharge.id;
                                    recurringCharge.JobRunDate = invoice.InvoiceEndDate.AddDays(1);
                                    recurringCharge.Invoice = "Inv" + max_invoice_id;
                                    _context.recurringCharges.Add(recurringCharge);
                                    _context.SaveChanges();


                                    // Update Job Table
                                    var recurreningjob = _context.recurringCharges.Where(x => x.RecurringChargeId == item.RecurringChargeId).FirstOrDefault();
                                    recurreningjob.IsRun = true;
                                    recurreningjob.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                    _context.recurringCharges.Update(recurreningjob);
                                    _context.SaveChanges();
                                    //Send Email
                                    var incoice_info = _context.invoices.Where(x => x.InvoiceId == max_invoice_id).FirstOrDefault();
                                    string body = string.Empty;
                                    _environment.WebRootPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                                    string contentRootPath = _environment.WebRootPath + "/htmltopdfP.html";
                                    string contentRootPath1 = _environment.WebRootPath + "/css/bootstrap.min.css";
                                    //Generate PDF
                                    using (StreamReader reader = new StreamReader(contentRootPath))
                                    {
                                        body = reader.ReadToEnd();
                                    }
                                    //Fill EMail By Parameter
                                    body = body.Replace("{title}", "Tamarran Payment Invoice");
                                    body = body.Replace("{currentdate}", DateTime.UtcNow.ToString("dd-MM-yyyy"));

                                    body = body.Replace("{InvocieStatus}", "Unpaid");
                                    body = body.Replace("{InvoiceID}", "Inv" + max_invoice_id);


                                    body = body.Replace("{User_Name}", getuserinfo.FullName);
                                    body = body.Replace("{User_Email}", user_Email);
                                    body = body.Replace("{User_GYM}", getuserinfo.GYMName);
                                    body = body.Replace("{User_Phone}", getuserinfo.PhoneNumber);

                                    body = body.Replace("{SubscriptionName}", getsubinfo.Name);
                                    body = body.Replace("{Discount}", Discount.ToString());
                                    body = body.Replace("{SubscriptionPeriod}", getuserinfo.Frequency);
                                    body = body.Replace("{SetupFee}", "0.0" + " " + getsubinfo.Currency);
                                    int amount = Convert.ToInt32(incoice_info.SubscriptionAmount);
                                    body = body.Replace("{SubscriptionAmount}", decimal.Round(sun_amount, 2).ToString() + " " + getsubinfo.Currency);
                                    //Calculate VAT
                                    if (getsubinfo.VAT == null || getsubinfo.VAT == "0")
                                    {
                                        body = body.Replace("{VAT}", "0.00");
                                        body = body.Replace("{Total}", amount.ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{InvoiceAmount}", amount.ToString() + " " + getsubinfo.Currency);
                                        var without_vat = Convert.ToDecimal(finalamount);
                                        body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(without_vat), 2).ToString() + " " + getsubinfo.Currency);
                                    }
                                    else
                                    {
                                        body = body.Replace("{VAT}", decimal.Round(Convert.ToDecimal(Vat), 2).ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{Total}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{InvoiceAmount}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                        var without_vat = Convert.ToDecimal(finalamount);
                                        body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(without_vat), 2).ToString() + " " + getsubinfo.Currency);
                                    }
                                    var bytes = (new NReco.PdfGenerator.HtmlToPdfConverter()).GeneratePdf(body);
                                    var bodyemail = EmailBodyFill.EmailBodyForBenefitPaymentRequest(getsubinfo, getuserinfo, myDeserializedClass.url);
                                    _ = _emailSender.SendEmailWithFIle(bytes, user_Email, "Tamarran - Automatic Payment Request", bodyemail, attachmentTitle);
                                }

                            }
                            else if (getuserinfo.Frequency == "QUARTERLY")
                            {
                                Random rnd = new Random();
                                var TransNo = "Txn_" + rnd.Next(10000000, 99999999);
                                var OrderNo = "Ord_" + rnd.Next(10000000, 99999999);
                                //var amount = decimal.Round(Convert.ToDecimal(item.Amount));
                                var description = getsubinfo.Frequency;
                                Reference reference = new Reference();
                                reference.transaction = TransNo;
                                reference.order = OrderNo;

                                long ExpireLink = new DateTimeOffset(DateTime.UtcNow.AddYears(1)).ToUnixTimeMilliseconds();
                                long Due = 0;
                                int days = DateTime.DaysInMonth(DateTime.UtcNow.Year, DateTime.UtcNow.Month);
                                decimal finalamount = 0;
                                decimal Discount = 0;
                                decimal Vat = 0;
                                decimal sun_amount = 0;

                                if (getuserinfo.Frequency == "DAILY")
                                {
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddDays(2)).ToUnixTimeMilliseconds();
                                    Discount = 0;
                                    finalamount = (decimal)Convert.ToInt32(getsubinfo.Amount) / (int)days;
                                }
                                else if (getuserinfo.Frequency == "WEEKLY")
                                {
                                    Discount = 0;
                                    finalamount = (decimal)Convert.ToInt32(getsubinfo.Amount) / 4;
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddDays(8)).ToUnixTimeMilliseconds();
                                }
                                else if (getuserinfo.Frequency == "MONTHLY")
                                {
                                    Discount = 0;
                                    finalamount = (decimal)Convert.ToInt32(getsubinfo.Amount);
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddMonths(1).AddDays(1)).ToUnixTimeMilliseconds();
                                }
                                else if (getuserinfo.Frequency == "QUARTERLY")
                                {
                                    Discount = 0;
                                    finalamount = (decimal)(Convert.ToInt32(getsubinfo.Amount) * 3) / 1;
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddMonths(3).AddDays(1)).ToUnixTimeMilliseconds();
                                }
                                else if (getuserinfo.Frequency == "HALFYEARLY")
                                {
                                    Discount = 0;
                                    finalamount = (decimal)(Convert.ToInt32(getsubinfo.Amount) * 6) / 1;
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddMonths(6).AddDays(1)).ToUnixTimeMilliseconds();
                                }
                                else if (getuserinfo.Frequency == "YEARLY")
                                {
                                    var amountpercentage = (decimal)(Convert.ToInt32(getsubinfo.Amount) / 100) * Convert.ToDecimal(getsubinfo.Discount);
                                    var final_amount_percentage = Convert.ToInt32(getsubinfo.Amount) - amountpercentage;
                                    finalamount = final_amount_percentage * 12;
                                    Discount = amountpercentage * 12;
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddYears(1).AddDays(1)).ToUnixTimeMilliseconds();
                                }
                                if (getsubinfo.VAT == null || getsubinfo.VAT == "0")
                                {
                                    Vat = 0;
                                }
                                else
                                {
                                    decimal totala = finalamount + Convert.ToDecimal(getsubinfo.SetupFee);
                                    sun_amount = totala;
                                    Vat = (decimal)((totala / Convert.ToInt32(getsubinfo.VAT)) * 100) / 100;
                                }
                                decimal after_vat_totalamount = finalamount + Convert.ToDecimal(getsubinfo.SetupFee) + Vat;

                                Redirect redirect = new Redirect();
                                redirect.url = Constants.RedirectURL + "/Home/CardVerifyBenefit";

                                Post post = new Post();
                                post.url = Constants.RedirectURL + "/Home/CardVerifyBenefits";

                                var countrycode = "";
                                if (getuserinfo.Country == "Bahrain")
                                {
                                    countrycode = "+973";
                                }
                                else if (getuserinfo.Country == "KSA")
                                {
                                    countrycode = "+966";
                                }
                                else if (getuserinfo.Country == "Kuwait")
                                {
                                    countrycode = "+965";
                                }
                                else if (getuserinfo.Country == "UAE")
                                {
                                    countrycode = "+971";
                                }
                                else if (getuserinfo.Country == "Qatar")
                                {
                                    countrycode = "+974";
                                }
                                else if (getuserinfo.Country == "Oman")
                                {
                                    countrycode = "+968";
                                }
                                var currency = getsubinfo.Currency;
                                Phone phone = new Phone();
                                phone.number = getuserinfo.PhoneNumber;
                                phone.country_code = countrycode;

                                Customer customer = new Customer();
                                customer.id = getuserinfo.Tap_CustomerID;

                                Receipt receipt = new Receipt();
                                receipt.sms = true;
                                receipt.email = true;

                                Notifications notifications = new Notifications();
                                List<string> receipts = new List<string>();
                                receipts.Add("SMS");
                                receipts.Add("EMAIL");
                                notifications.channels = receipts;
                                notifications.dispatch = true;

                                List<string> currencies = new List<string>();
                                currencies.Add(getsubinfo.Currency);

                                Charge charge = new Charge();
                                charge.receipt = receipt;
                                charge.statement_descriptor = "test";

                                List<string> p_methods = new List<string>();
                                p_methods.Add("BENEFIT");

                                List<Item> items = new List<Item>();
                                Item itemss = new Item();
                                itemss.image = "";
                                itemss.quantity = 1;
                                itemss.name = "Invoice Amount";
                                itemss.amount = Math.Round(after_vat_totalamount, 2).ToString("0.00");
                                itemss.currency = getsubinfo.Currency;
                                items.Add(itemss);

                                Order order = new Order();
                                order.amount = Math.Round(after_vat_totalamount, 2).ToString("0.00");
                                order.currency = getsubinfo.Currency;
                                order.items = items;


                                TapInvoice tapInvoice = new TapInvoice();
                                tapInvoice.redirect = redirect;
                                tapInvoice.post = post;
                                tapInvoice.customer = customer;
                                tapInvoice.draft = false;
                                tapInvoice.due = Due;
                                tapInvoice.expiry = ExpireLink;
                                tapInvoice.description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")";
                                tapInvoice.mode = "INVOICE";
                                tapInvoice.note = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")";
                                tapInvoice.notifications = notifications;
                                tapInvoice.currencies = currencies;
                                tapInvoice.charge = charge;
                                tapInvoice.payment_methods = p_methods;
                                tapInvoice.reference = reference;
                                tapInvoice.order = order;


                                var jsonmodel = JsonConvert.SerializeObject(tapInvoice);
                                var client = new HttpClient();
                                var request = new HttpRequestMessage
                                {
                                    Method = HttpMethod.Post,
                                    RequestUri = new Uri("https://api.tap.company/v2/invoices/"),
                                    Headers =
                                      {
                                          { "accept", "application/json" },
                                          { "Authorization", "Bearer " + getuserinfo.SecertKey },
                                      },
                                    Content = new StringContent(jsonmodel)
                                    {
                                        Headers =
                                      {
                                          ContentType = new MediaTypeHeaderValue("application/json")
                                      }
                                    }
                                };
                                TapInvoiceResponse myDeserializedClass = null;
                                using (var response = await client.SendAsync(request))
                                {
                                    var bodys = await response.Content.ReadAsStringAsync();
                                    myDeserializedClass = JsonConvert.DeserializeObject<TapInvoiceResponse>(bodys);
                                }
                                if (myDeserializedClass.status == "CREATED")
                                {
                                    Invoice invoice = new Invoice
                                    {
                                        InvoiceStartDate = DateTime.UtcNow,
                                        InvoiceEndDate = DateTime.UtcNow.AddMonths(3),
                                        Currency = getsubinfo.Currency,
                                        AddedDate = DateTime.UtcNow,
                                        AddedBy = getuserinfo.FullName,
                                        SubscriptionAmount = Convert.ToDouble(decimal.Round(after_vat_totalamount, 2)),
                                        SubscriptionId = Convert.ToInt32(getsubinfo.SubscriptionId),
                                        Status = "Un-Paid",
                                        IsDeleted = false,
                                        VAT = Vat.ToString(),
                                        Discount = Discount.ToString(),
                                        Description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")",
                                        SubscriptionName = getsubinfo.Name,
                                        UserId = getuserinfo.Id,
                                        ChargeId = myDeserializedClass.id,
                                        GymName = getuserinfo.GYMName,
                                        Country = getsubinfo.Countries
                                    };
                                    _context.invoices.Add(invoice);
                                    _context.SaveChanges();
                                    int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
                                    //Next Recurrening Job Date
                                    RecurringCharge recurringCharge = new RecurringCharge();
                                    recurringCharge.Amount = Convert.ToDecimal(item.Amount);
                                    recurringCharge.SubscriptionId = getsubinfo.SubscriptionId;
                                    recurringCharge.UserID = getuserinfo.Id;
                                    recurringCharge.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                    recurringCharge.ChargeId = deserialized_CreateCharge.id;
                                    recurringCharge.JobRunDate = invoice.InvoiceEndDate.AddDays(1);
                                    recurringCharge.Invoice = "Inv" + max_invoice_id;
                                    _context.recurringCharges.Add(recurringCharge);
                                    _context.SaveChanges();


                                    // Update Job Table
                                    var recurreningjob = _context.recurringCharges.Where(x => x.RecurringChargeId == item.RecurringChargeId).FirstOrDefault();
                                    recurreningjob.IsRun = true;
                                    recurreningjob.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                    _context.recurringCharges.Update(recurreningjob);
                                    _context.SaveChanges();
                                    //Send Email
                                    var incoice_info = _context.invoices.Where(x => x.InvoiceId == max_invoice_id).FirstOrDefault();
                                    string body = string.Empty;
                                    _environment.WebRootPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                                    string contentRootPath = _environment.WebRootPath + "/htmltopdfP.html";
                                    string contentRootPath1 = _environment.WebRootPath + "/css/bootstrap.min.css";
                                    //Generate PDF
                                    using (StreamReader reader = new StreamReader(contentRootPath))
                                    {
                                        body = reader.ReadToEnd();
                                    }
                                    //Fill EMail By Parameter
                                    body = body.Replace("{title}", "Tamarran Payment Invoice");
                                    body = body.Replace("{currentdate}", DateTime.UtcNow.ToString("dd-MM-yyyy"));

                                    body = body.Replace("{InvocieStatus}", "Unpaid");
                                    body = body.Replace("{InvoiceID}", "Inv" + max_invoice_id);


                                    body = body.Replace("{User_Name}", getuserinfo.FullName);
                                    body = body.Replace("{User_Email}", user_Email);
                                    body = body.Replace("{User_GYM}", getuserinfo.GYMName);
                                    body = body.Replace("{User_Phone}", getuserinfo.PhoneNumber);

                                    body = body.Replace("{SubscriptionName}", getsubinfo.Name);
                                    body = body.Replace("{Discount}", Discount.ToString());
                                    body = body.Replace("{SubscriptionPeriod}", getuserinfo.Frequency);
                                    body = body.Replace("{SetupFee}", "0.0" + " " + getsubinfo.Currency);
                                    int amount = Convert.ToInt32(incoice_info.SubscriptionAmount);
                                    body = body.Replace("{SubscriptionAmount}", decimal.Round(sun_amount, 2).ToString() + " " + getsubinfo.Currency);
                                    //Calculate VAT
                                    if (getsubinfo.VAT == null || getsubinfo.VAT == "0")
                                    {
                                        body = body.Replace("{VAT}", "0.00");
                                        body = body.Replace("{Total}", amount.ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{InvoiceAmount}", amount.ToString() + " " + getsubinfo.Currency);
                                        var without_vat = Convert.ToDecimal(finalamount);
                                        body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(without_vat), 2).ToString() + " " + getsubinfo.Currency);
                                    }
                                    else
                                    {
                                        body = body.Replace("{VAT}", decimal.Round(Convert.ToDecimal(Vat), 2).ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{Total}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{InvoiceAmount}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                        var without_vat = Convert.ToDecimal(finalamount);
                                        body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(without_vat), 2).ToString() + " " + getsubinfo.Currency);
                                    }
                                    var bytes = (new NReco.PdfGenerator.HtmlToPdfConverter()).GeneratePdf(body);
                                    var bodyemail = EmailBodyFill.EmailBodyForBenefitPaymentRequest(getsubinfo, getuserinfo, myDeserializedClass.url);
                                    _ = _emailSender.SendEmailWithFIle(bytes, user_Email, "Tamarran - Automatic Payment Request", bodyemail, attachmentTitle);
                                }
                            }
                            else if (getuserinfo.Frequency == "HALFYEARLY")
                            {
                                Random rnd = new Random();
                                var TransNo = "Txn_" + rnd.Next(10000000, 99999999);
                                var OrderNo = "Ord_" + rnd.Next(10000000, 99999999);
                                //var amount = decimal.Round(Convert.ToDecimal(item.Amount));
                                var description = getsubinfo.Frequency;
                                Reference reference = new Reference();
                                reference.transaction = TransNo;
                                reference.order = OrderNo;

                                long ExpireLink = new DateTimeOffset(DateTime.UtcNow.AddYears(1)).ToUnixTimeMilliseconds();
                                long Due = 0;
                                int days = DateTime.DaysInMonth(DateTime.UtcNow.Year, DateTime.UtcNow.Month);
                                decimal finalamount = 0;
                                decimal Discount = 0;
                                decimal Vat = 0;
                                decimal sun_amount = 0;

                                if (getuserinfo.Frequency == "DAILY")
                                {
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddDays(2)).ToUnixTimeMilliseconds();
                                    Discount = 0;
                                    finalamount = (decimal)Convert.ToInt32(getsubinfo.Amount) / (int)days;
                                }
                                else if (getuserinfo.Frequency == "WEEKLY")
                                {
                                    Discount = 0;
                                    finalamount = (decimal)Convert.ToInt32(getsubinfo.Amount) / 4;
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddDays(8)).ToUnixTimeMilliseconds();
                                }
                                else if (getuserinfo.Frequency == "MONTHLY")
                                {
                                    Discount = 0;
                                    finalamount = (decimal)Convert.ToInt32(getsubinfo.Amount);
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddMonths(1).AddDays(1)).ToUnixTimeMilliseconds();
                                }
                                else if (getuserinfo.Frequency == "QUARTERLY")
                                {
                                    Discount = 0;
                                    finalamount = (decimal)(Convert.ToInt32(getsubinfo.Amount) * 3) / 1;
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddMonths(3).AddDays(1)).ToUnixTimeMilliseconds();
                                }
                                else if (getuserinfo.Frequency == "HALFYEARLY")
                                {
                                    Discount = 0;
                                    finalamount = (decimal)(Convert.ToInt32(getsubinfo.Amount) * 6) / 1;
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddMonths(6).AddDays(1)).ToUnixTimeMilliseconds();
                                }
                                else if (getuserinfo.Frequency == "YEARLY")
                                {
                                    var amountpercentage = (decimal)(Convert.ToInt32(getsubinfo.Amount) / 100) * Convert.ToDecimal(getsubinfo.Discount);
                                    var final_amount_percentage = Convert.ToInt32(getsubinfo.Amount) - amountpercentage;
                                    finalamount = final_amount_percentage * 12;
                                    Discount = amountpercentage * 12;
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddYears(1).AddDays(1)).ToUnixTimeMilliseconds();
                                }
                                if (getsubinfo.VAT == null || getsubinfo.VAT == "0")
                                {
                                    Vat = 0;
                                }
                                else
                                {
                                    decimal totala = finalamount + Convert.ToDecimal(getsubinfo.SetupFee);
                                    sun_amount = totala;
                                    Vat = (decimal)((totala / Convert.ToInt32(getsubinfo.VAT)) * 100) / 100;
                                }
                                decimal after_vat_totalamount = finalamount + Convert.ToDecimal(getsubinfo.SetupFee) + Vat;

                                Redirect redirect = new Redirect();
                                redirect.url = Constants.RedirectURL + "/Home/CardVerifyBenefit";

                                Post post = new Post();
                                post.url = Constants.RedirectURL + "/Home/CardVerifyBenefits";

                                var countrycode = "";
                                if (getuserinfo.Country == "Bahrain")
                                {
                                    countrycode = "+973";
                                }
                                else if (getuserinfo.Country == "KSA")
                                {
                                    countrycode = "+966";
                                }
                                else if (getuserinfo.Country == "Kuwait")
                                {
                                    countrycode = "+965";
                                }
                                else if (getuserinfo.Country == "UAE")
                                {
                                    countrycode = "+971";
                                }
                                else if (getuserinfo.Country == "Qatar")
                                {
                                    countrycode = "+974";
                                }
                                else if (getuserinfo.Country == "Oman")
                                {
                                    countrycode = "+968";
                                }
                                var currency = getsubinfo.Currency;
                                Phone phone = new Phone();
                                phone.number = getuserinfo.PhoneNumber;
                                phone.country_code = countrycode;

                                Customer customer = new Customer();
                                customer.id = getuserinfo.Tap_CustomerID;

                                Receipt receipt = new Receipt();
                                receipt.sms = true;
                                receipt.email = true;

                                Notifications notifications = new Notifications();
                                List<string> receipts = new List<string>();
                                receipts.Add("SMS");
                                receipts.Add("EMAIL");
                                notifications.channels = receipts;
                                notifications.dispatch = true;

                                List<string> currencies = new List<string>();
                                currencies.Add(getsubinfo.Currency);

                                Charge charge = new Charge();
                                charge.receipt = receipt;
                                charge.statement_descriptor = "test";

                                List<string> p_methods = new List<string>();
                                p_methods.Add("BENEFIT");

                                List<Item> items = new List<Item>();
                                Item itemss = new Item();
                                itemss.image = "";
                                itemss.quantity = 1;
                                itemss.name = "Invoice Amount";
                                itemss.amount = Math.Round(after_vat_totalamount, 2).ToString("0.00");
                                itemss.currency = getsubinfo.Currency;
                                items.Add(itemss);

                                Order order = new Order();
                                order.amount = Math.Round(after_vat_totalamount, 2).ToString("0.00");
                                order.currency = getsubinfo.Currency;
                                order.items = items;


                                TapInvoice tapInvoice = new TapInvoice();
                                tapInvoice.redirect = redirect;
                                tapInvoice.post = post;
                                tapInvoice.customer = customer;
                                tapInvoice.draft = false;
                                tapInvoice.due = Due;
                                tapInvoice.expiry = ExpireLink;
                                tapInvoice.description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")";
                                tapInvoice.mode = "INVOICE";
                                tapInvoice.note = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")";
                                tapInvoice.notifications = notifications;
                                tapInvoice.currencies = currencies;
                                tapInvoice.charge = charge;
                                tapInvoice.payment_methods = p_methods;
                                tapInvoice.reference = reference;
                                tapInvoice.order = order;


                                var jsonmodel = JsonConvert.SerializeObject(tapInvoice);
                                var client = new HttpClient();
                                var request = new HttpRequestMessage
                                {
                                    Method = HttpMethod.Post,
                                    RequestUri = new Uri("https://api.tap.company/v2/invoices/"),
                                    Headers =
                                      {
                                          { "accept", "application/json" },
                                          { "Authorization", "Bearer " + getuserinfo.SecertKey },
                                      },
                                    Content = new StringContent(jsonmodel)
                                    {
                                        Headers =
                                      {
                                          ContentType = new MediaTypeHeaderValue("application/json")
                                      }
                                    }
                                };
                                TapInvoiceResponse myDeserializedClass = null;
                                using (var response = await client.SendAsync(request))
                                {
                                    var bodys = await response.Content.ReadAsStringAsync();
                                    myDeserializedClass = JsonConvert.DeserializeObject<TapInvoiceResponse>(bodys);
                                }
                                if (myDeserializedClass.status == "CREATED")
                                {
                                    Invoice invoice = new Invoice
                                    {
                                        InvoiceStartDate = DateTime.UtcNow,
                                        InvoiceEndDate = DateTime.UtcNow.AddMonths(6),
                                        Currency = getsubinfo.Currency,
                                        AddedDate = DateTime.UtcNow,
                                        AddedBy = getuserinfo.FullName,
                                        SubscriptionAmount = Convert.ToDouble(decimal.Round(after_vat_totalamount, 2)),
                                        SubscriptionId = Convert.ToInt32(getsubinfo.SubscriptionId),
                                        Status = "Un-Paid",
                                        IsDeleted = false,
                                        VAT = Vat.ToString(),
                                        Discount = Discount.ToString(),
                                        Description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")",
                                        SubscriptionName = getsubinfo.Name,
                                        UserId = getuserinfo.Id,
                                        ChargeId = myDeserializedClass.id,
                                        GymName = getuserinfo.GYMName,
                                        Country = getsubinfo.Countries
                                    };
                                    _context.invoices.Add(invoice);
                                    _context.SaveChanges();
                                    int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
                                    //Next Recurrening Job Date
                                    RecurringCharge recurringCharge = new RecurringCharge();
                                    recurringCharge.Amount = Convert.ToDecimal(item.Amount);
                                    recurringCharge.SubscriptionId = getsubinfo.SubscriptionId;
                                    recurringCharge.UserID = getuserinfo.Id;
                                    recurringCharge.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                    recurringCharge.ChargeId = deserialized_CreateCharge.id;
                                    recurringCharge.JobRunDate = invoice.InvoiceEndDate.AddDays(1);
                                    recurringCharge.Invoice = "Inv" + max_invoice_id;
                                    _context.recurringCharges.Add(recurringCharge);
                                    _context.SaveChanges();


                                    // Update Job Table
                                    var recurreningjob = _context.recurringCharges.Where(x => x.RecurringChargeId == item.RecurringChargeId).FirstOrDefault();
                                    recurreningjob.IsRun = true;
                                    recurreningjob.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                    _context.recurringCharges.Update(recurreningjob);
                                    _context.SaveChanges();
                                    //Send Email
                                    var incoice_info = _context.invoices.Where(x => x.InvoiceId == max_invoice_id).FirstOrDefault();
                                    string body = string.Empty;
                                    _environment.WebRootPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                                    string contentRootPath = _environment.WebRootPath + "/htmltopdfP.html";
                                    string contentRootPath1 = _environment.WebRootPath + "/css/bootstrap.min.css";
                                    //Generate PDF
                                    using (StreamReader reader = new StreamReader(contentRootPath))
                                    {
                                        body = reader.ReadToEnd();
                                    }
                                    //Fill EMail By Parameter
                                    body = body.Replace("{title}", "Tamarran Payment Invoice");
                                    body = body.Replace("{currentdate}", DateTime.UtcNow.ToString("dd-MM-yyyy"));

                                    body = body.Replace("{InvocieStatus}", "Unpaid");
                                    body = body.Replace("{InvoiceID}", "Inv" + max_invoice_id);


                                    body = body.Replace("{User_Name}", getuserinfo.FullName);
                                    body = body.Replace("{User_Email}", user_Email);
                                    body = body.Replace("{User_GYM}", getuserinfo.GYMName);
                                    body = body.Replace("{User_Phone}", getuserinfo.PhoneNumber);

                                    body = body.Replace("{SubscriptionName}", getsubinfo.Name);
                                    body = body.Replace("{Discount}", Discount.ToString());
                                    body = body.Replace("{SubscriptionPeriod}", getuserinfo.Frequency);
                                    body = body.Replace("{SetupFee}", "0.0" + " " + getsubinfo.Currency);
                                    int amount = Convert.ToInt32(incoice_info.SubscriptionAmount);
                                    body = body.Replace("{SubscriptionAmount}", decimal.Round(sun_amount, 2).ToString() + " " + getsubinfo.Currency);
                                    //Calculate VAT
                                    if (getsubinfo.VAT == null || getsubinfo.VAT == "0")
                                    {
                                        body = body.Replace("{VAT}", "0.00");
                                        body = body.Replace("{Total}", amount.ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{InvoiceAmount}", amount.ToString() + " " + getsubinfo.Currency);
                                        var without_vat = Convert.ToDecimal(finalamount);
                                        body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(without_vat), 2).ToString() + " " + getsubinfo.Currency);
                                    }
                                    else
                                    {
                                        body = body.Replace("{VAT}", decimal.Round(Convert.ToDecimal(Vat), 2).ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{Total}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{InvoiceAmount}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                        var without_vat = Convert.ToDecimal(finalamount);
                                        body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(without_vat), 2).ToString() + " " + getsubinfo.Currency);
                                    }
                                    var bytes = (new NReco.PdfGenerator.HtmlToPdfConverter()).GeneratePdf(body);
                                    var bodyemail = EmailBodyFill.EmailBodyForBenefitPaymentRequest(getsubinfo, getuserinfo, myDeserializedClass.url);
                                    _ = _emailSender.SendEmailWithFIle(bytes, user_Email, "Tamarran - Automatic Payment Request", bodyemail, attachmentTitle);
                                }
                            }
                            else if (getuserinfo.Frequency == "YEARLY")
                            {
                                Random rnd = new Random();
                                var TransNo = "Txn_" + rnd.Next(10000000, 99999999);
                                var OrderNo = "Ord_" + rnd.Next(10000000, 99999999);
                                //var amount = decimal.Round(Convert.ToDecimal(item.Amount));
                                var description = getsubinfo.Frequency;
                                Reference reference = new Reference();
                                reference.transaction = TransNo;
                                reference.order = OrderNo;

                                long ExpireLink = new DateTimeOffset(DateTime.UtcNow.AddYears(1)).ToUnixTimeMilliseconds();
                                long Due = 0;
                                int days = DateTime.DaysInMonth(DateTime.UtcNow.Year, DateTime.UtcNow.Month);
                                decimal finalamount = 0;
                                decimal Discount = 0;
                                decimal Vat = 0;
                                decimal sun_amount = 0;

                                if (getuserinfo.Frequency == "DAILY")
                                {
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddDays(2)).ToUnixTimeMilliseconds();
                                    Discount = 0;
                                    finalamount = (decimal)Convert.ToInt32(getsubinfo.Amount) / (int)days;
                                }
                                else if (getuserinfo.Frequency == "WEEKLY")
                                {
                                    Discount = 0;
                                    finalamount = (decimal)Convert.ToInt32(getsubinfo.Amount) / 4;
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddDays(8)).ToUnixTimeMilliseconds();
                                }
                                else if (getuserinfo.Frequency == "MONTHLY")
                                {
                                    Discount = 0;
                                    finalamount = (decimal)Convert.ToInt32(getsubinfo.Amount);
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddMonths(1).AddDays(1)).ToUnixTimeMilliseconds();
                                }
                                else if (getuserinfo.Frequency == "QUARTERLY")
                                {
                                    Discount = 0;
                                    finalamount = (decimal)(Convert.ToInt32(getsubinfo.Amount) * 3) / 1;
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddMonths(3).AddDays(1)).ToUnixTimeMilliseconds();
                                }
                                else if (getuserinfo.Frequency == "HALFYEARLY")
                                {
                                    Discount = 0;
                                    finalamount = (decimal)(Convert.ToInt32(getsubinfo.Amount) * 6) / 1;
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddMonths(6).AddDays(1)).ToUnixTimeMilliseconds();
                                }
                                else if (getuserinfo.Frequency == "YEARLY")
                                {
                                    var amountpercentage = (decimal)(Convert.ToInt32(getsubinfo.Amount) / 100) * Convert.ToDecimal(getsubinfo.Discount);
                                    var final_amount_percentage = Convert.ToInt32(getsubinfo.Amount) - amountpercentage;
                                    finalamount = final_amount_percentage * 12;
                                    Discount = amountpercentage * 12;
                                    Due = new DateTimeOffset(DateTime.UtcNow.AddYears(1).AddDays(1)).ToUnixTimeMilliseconds();
                                }
                                if (getsubinfo.VAT == null || getsubinfo.VAT == "0")
                                {
                                    Vat = 0;
                                }
                                else
                                {
                                    decimal totala = finalamount + Convert.ToDecimal(getsubinfo.SetupFee);
                                    sun_amount = totala;
                                    Vat = (decimal)((totala / Convert.ToInt32(getsubinfo.VAT)) * 100) / 100;
                                }
                                decimal after_vat_totalamount = finalamount + Convert.ToDecimal(getsubinfo.SetupFee) + Vat;

                                Redirect redirect = new Redirect();
                                redirect.url = Constants.RedirectURL + "/Home/CardVerifyBenefit";

                                Post post = new Post();
                                post.url = Constants.RedirectURL + "/Home/CardVerifyBenefits";

                                var countrycode = "";
                                if (getuserinfo.Country == "Bahrain")
                                {
                                    countrycode = "+973";
                                }
                                else if (getuserinfo.Country == "KSA")
                                {
                                    countrycode = "+966";
                                }
                                else if (getuserinfo.Country == "Kuwait")
                                {
                                    countrycode = "+965";
                                }
                                else if (getuserinfo.Country == "UAE")
                                {
                                    countrycode = "+971";
                                }
                                else if (getuserinfo.Country == "Qatar")
                                {
                                    countrycode = "+974";
                                }
                                else if (getuserinfo.Country == "Oman")
                                {
                                    countrycode = "+968";
                                }
                                var currency = getsubinfo.Currency;
                                Phone phone = new Phone();
                                phone.number = getuserinfo.PhoneNumber;
                                phone.country_code = countrycode;

                                Customer customer = new Customer();
                                customer.id = getuserinfo.Tap_CustomerID;

                                Receipt receipt = new Receipt();
                                receipt.sms = true;
                                receipt.email = true;

                                Notifications notifications = new Notifications();
                                List<string> receipts = new List<string>();
                                receipts.Add("SMS");
                                receipts.Add("EMAIL");
                                notifications.channels = receipts;
                                notifications.dispatch = true;

                                List<string> currencies = new List<string>();
                                currencies.Add(getsubinfo.Currency);

                                Charge charge = new Charge();
                                charge.receipt = receipt;
                                charge.statement_descriptor = "test";

                                List<string> p_methods = new List<string>();
                                p_methods.Add("BENEFIT");

                                List<Item> items = new List<Item>();
                                Item itemss = new Item();
                                itemss.image = "";
                                itemss.quantity = 1;
                                itemss.name = "Invoice Amount";
                                itemss.amount = Math.Round(after_vat_totalamount, 2).ToString("0.00");
                                itemss.currency = getsubinfo.Currency;
                                items.Add(itemss);

                                Order order = new Order();
                                order.amount = Math.Round(after_vat_totalamount, 2).ToString("0.00");
                                order.currency = getsubinfo.Currency;
                                order.items = items;


                                TapInvoice tapInvoice = new TapInvoice();
                                tapInvoice.redirect = redirect;
                                tapInvoice.post = post;
                                tapInvoice.customer = customer;
                                tapInvoice.draft = false;
                                tapInvoice.due = Due;
                                tapInvoice.expiry = ExpireLink;
                                tapInvoice.description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")";
                                tapInvoice.mode = "INVOICE";
                                tapInvoice.note = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")";
                                tapInvoice.notifications = notifications;
                                tapInvoice.currencies = currencies;
                                tapInvoice.charge = charge;
                                tapInvoice.payment_methods = p_methods;
                                tapInvoice.reference = reference;
                                tapInvoice.order = order;


                                var jsonmodel = JsonConvert.SerializeObject(tapInvoice);
                                var client = new HttpClient();
                                var request = new HttpRequestMessage
                                {
                                    Method = HttpMethod.Post,
                                    RequestUri = new Uri("https://api.tap.company/v2/invoices/"),
                                    Headers =
                                      {
                                          { "accept", "application/json" },
                                          { "Authorization", "Bearer " + getuserinfo.SecertKey },
                                      },
                                    Content = new StringContent(jsonmodel)
                                    {
                                        Headers =
                                      {
                                          ContentType = new MediaTypeHeaderValue("application/json")
                                      }
                                    }
                                };
                                TapInvoiceResponse myDeserializedClass = null;
                                using (var response = await client.SendAsync(request))
                                {
                                    var bodys = await response.Content.ReadAsStringAsync();
                                    myDeserializedClass = JsonConvert.DeserializeObject<TapInvoiceResponse>(bodys);
                                }
                                if (myDeserializedClass.status == "CREATED")
                                {
                                    Invoice invoice = new Invoice
                                    {
                                        InvoiceStartDate = DateTime.UtcNow,
                                        InvoiceEndDate = DateTime.UtcNow.AddYears(1),
                                        Currency = getsubinfo.Currency,
                                        AddedDate = DateTime.UtcNow,
                                        AddedBy = getuserinfo.FullName,
                                        SubscriptionAmount = Convert.ToDouble(decimal.Round(after_vat_totalamount, 2)),
                                        SubscriptionId = Convert.ToInt32(getsubinfo.SubscriptionId),
                                        Status = "Un-Paid",
                                        IsDeleted = false,
                                        VAT = Vat.ToString(),
                                        Discount = Discount.ToString(),
                                        Description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")",
                                        SubscriptionName = getsubinfo.Name,
                                        UserId = getuserinfo.Id,
                                        ChargeId = myDeserializedClass.id,
                                        GymName = getuserinfo.GYMName,
                                        Country = getsubinfo.Countries
                                    };
                                    _context.invoices.Add(invoice);
                                    _context.SaveChanges();
                                    int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
                                    //Next Recurrening Job Date
                                    RecurringCharge recurringCharge = new RecurringCharge();
                                    recurringCharge.Amount = Convert.ToDecimal(item.Amount);
                                    recurringCharge.SubscriptionId = getsubinfo.SubscriptionId;
                                    recurringCharge.UserID = getuserinfo.Id;
                                    recurringCharge.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                    recurringCharge.ChargeId = deserialized_CreateCharge.id;
                                    recurringCharge.JobRunDate = invoice.InvoiceEndDate.AddDays(1);
                                    recurringCharge.Invoice = "Inv" + max_invoice_id;
                                    _context.recurringCharges.Add(recurringCharge);
                                    _context.SaveChanges();


                                    // Update Job Table
                                    var recurreningjob = _context.recurringCharges.Where(x => x.RecurringChargeId == item.RecurringChargeId).FirstOrDefault();
                                    recurreningjob.IsRun = true;
                                    recurreningjob.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                    _context.recurringCharges.Update(recurreningjob);
                                    _context.SaveChanges();
                                    //Send Email
                                    var incoice_info = _context.invoices.Where(x => x.InvoiceId == max_invoice_id).FirstOrDefault();
                                    string body = string.Empty;
                                    _environment.WebRootPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                                    string contentRootPath = _environment.WebRootPath + "/htmltopdfP.html";
                                    string contentRootPath1 = _environment.WebRootPath + "/css/bootstrap.min.css";
                                    //Generate PDF
                                    using (StreamReader reader = new StreamReader(contentRootPath))
                                    {
                                        body = reader.ReadToEnd();
                                    }
                                    //Fill EMail By Parameter
                                    body = body.Replace("{title}", "Tamarran Payment Invoice");
                                    body = body.Replace("{currentdate}", DateTime.UtcNow.ToString("dd-MM-yyyy"));

                                    body = body.Replace("{InvocieStatus}", "Unpaid");
                                    body = body.Replace("{InvoiceID}", "Inv" + max_invoice_id);


                                    body = body.Replace("{User_Name}", getuserinfo.FullName);
                                    body = body.Replace("{User_Email}", user_Email);
                                    body = body.Replace("{User_GYM}", getuserinfo.GYMName);
                                    body = body.Replace("{User_Phone}", getuserinfo.PhoneNumber);

                                    body = body.Replace("{SubscriptionName}", getsubinfo.Name);
                                    body = body.Replace("{Discount}", Discount.ToString());
                                    body = body.Replace("{SubscriptionPeriod}", getuserinfo.Frequency);
                                    body = body.Replace("{SetupFee}", "0.0" + " " + getsubinfo.Currency);
                                    int amount = Convert.ToInt32(incoice_info.SubscriptionAmount);
                                    body = body.Replace("{SubscriptionAmount}", decimal.Round(sun_amount, 2).ToString() + " " + getsubinfo.Currency);
                                    //Calculate VAT
                                    if (getsubinfo.VAT == null || getsubinfo.VAT == "0")
                                    {
                                        body = body.Replace("{VAT}", "0.00");
                                        body = body.Replace("{Total}", amount.ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{InvoiceAmount}", amount.ToString() + " " + getsubinfo.Currency);
                                        var without_vat = Convert.ToDecimal(finalamount);
                                        body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(without_vat), 2).ToString() + " " + getsubinfo.Currency);
                                    }
                                    else
                                    {
                                        body = body.Replace("{VAT}", decimal.Round(Convert.ToDecimal(Vat), 2).ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{Total}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                        body = body.Replace("{InvoiceAmount}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                        var without_vat = Convert.ToDecimal(finalamount);
                                        body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(without_vat), 2).ToString() + " " + getsubinfo.Currency);
                                    }
                                    var bytes = (new NReco.PdfGenerator.HtmlToPdfConverter()).GeneratePdf(body);
                                    var bodyemail = EmailBodyFill.EmailBodyForBenefitPaymentRequest(getsubinfo, getuserinfo, myDeserializedClass.url);
                                    _ = _emailSender.SendEmailWithFIle(bytes, user_Email, "Tamarran - Automatic Payment Request", bodyemail, attachmentTitle);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
