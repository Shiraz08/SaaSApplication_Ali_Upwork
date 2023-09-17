using Microsoft.AspNetCore.Identity;
using Newtonsoft.Json;
using TapPaymentIntegration.Controllers;
using TapPaymentIntegration.Data;
using TapPaymentIntegration.Models.Email;
using TapPaymentIntegration.Models.InvoiceDTO;
using TapPaymentIntegration.Models.PaymentDTO;
using ApplicationUser = TapPaymentIntegration.Areas.Identity.Data.ApplicationUser;

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
        public async System.Threading.Tasks.Task AutoChargeJob() 
        {
            //var recurringCharges_list = _context.recurringCharges.Where(x => x.JobRunDate.Date == DateTime.Now.Date && x.IsRun == false).ToList();
            var recurringCharges_list = _context.recurringCharges.Where(x => x.JobRunDate.Date == DateTime.Now.AddDays(1).Date && x.IsRun == false).ToList();
            foreach (var item in recurringCharges_list)
            {
                var getsubinfo = _context.subscriptions.Where(x => x.SubscriptionId == item.SubscriptionId).FirstOrDefault();
                var getuserinfo = _context.Users.Where(x => x.Id == item.UserID).FirstOrDefault();
                if (getuserinfo != null)
                {
                    if (getuserinfo.SubscribeID > 0 && getuserinfo.Status == true)
                    {
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
                        int days = DateTime.DaysInMonth(DateTime.Now.Year, DateTime.Now.Month);
                        Random rnd = new Random();
                        var TransNo = "Txn_" + rnd.Next(10000000, 99999999);
                        var OrderNo = "Ord_" + rnd.Next(10000000, 99999999);
                        //Create Invoice 
                        if (getuserinfo.Frequency == "DAILY")
                        {
                            int calculate_amount = Convert.ToInt32(getsubinfo.Amount);
                            var finalamount = calculate_amount / days;
                            // Create a charge
                            var client = new HttpClient();
                            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tap.company/v2/charges");
                            request.Headers.Add("Authorization", "Bearer " + getuserinfo.SecertKey);
                            request.Headers.Add("accept", "application/json");
                            var content = new StringContent("\r\n{\r\n  \"amount\": " + Decimal.Round(finalamount) + ",\r\n  \"currency\": \"" + getsubinfo.Currency + "\",\r\n  \"customer_initiated\": false,\r\n  \"threeDSecure\": true,\r\n  \"save_card\": false,\r\n  \"payment_agreement\": {\r\n    \"contract\": {\r\n      \"id\": \"" + getuserinfo.Tap_Card_ID + "\"\r\n    },\r\n    \"id\": \"" + getuserinfo.Tap_Agreement_ID + "\"\r\n  },\r\n  \"receipt\": {\r\n    \"email\": true,\r\n    \"sms\": true\r\n  },\"reference\": {\r\n    \"transaction\": \"" + TransNo + "\",\r\n    \"order\": \"" + OrderNo + "\"\r\n  },\r\n  \"customer\": {\r\n    \"id\": \"" + getuserinfo.Tap_CustomerID + "\"\r\n  },\r\n  \"merchant\": {\r\n    \"id\": \"22116401\"\r\n  },\r\n  \"source\": {\r\n    \"id\": \"" + Deserialized_savecard.id + "\"\r\n  },\r\n  \"redirect\": {\r\n    \"url\": \"https://test.com/\"\r\n  }\r\n}\r\n", null, "application/json");
                            request.Content = content;
                            var response = await client.SendAsync(request);
                            var bodys = await response.Content.ReadAsStringAsync();
                            CreateCharge deserialized_CreateCharge = JsonConvert.DeserializeObject<CreateCharge>(bodys);
                            if (deserialized_CreateCharge.status == "CAPTURED")
                            {
                                Invoice invoice = new Invoice
                                {
                                    InvoiceStartDate = DateTime.Now,
                                    InvoiceEndDate = DateTime.Now,
                                    AddedDate = DateTime.Now,
                                    AddedBy = getuserinfo.FullName,
                                    SubscriptionAmount = finalamount,
                                    SubscriptionId = getsubinfo.SubscriptionId,
                                    Status = "Payment Captured",
                                    IsDeleted = false,
                                    Description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")",
                                    SubscriptionName = getsubinfo.Name,
                                    UserId = getuserinfo.Id,
                                    ChargeId = deserialized_CreateCharge.id,
                                };
                                _context.invoices.Add(invoice);
                                _context.SaveChanges();
                                //Next Recurrening Job Date
                                RecurringCharge recurringCharge = new RecurringCharge();
                                recurringCharge.Amount = Convert.ToDecimal(item.Amount);
                                recurringCharge.SubscriptionId = getsubinfo.SubscriptionId;
                                recurringCharge.UserID = getuserinfo.Id;
                                recurringCharge.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                recurringCharge.ChargeId = deserialized_CreateCharge.id;
                                recurringCharge.JobRunDate = invoice.InvoiceEndDate.AddDays(1);
                                _context.recurringCharges.Add(recurringCharge);
                                _context.SaveChanges();
                            }
                        }
                        else if (getuserinfo.Frequency == "WEEKLY")
                        {
                            var calculate_amount = getsubinfo.Amount;
                            int finalamount = Convert.ToInt32(calculate_amount) / 4;
                            // Create a charge
                            var client = new HttpClient();
                            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tap.company/v2/charges");
                            request.Headers.Add("Authorization", "Bearer " + getuserinfo.SecertKey);
                            request.Headers.Add("accept", "application/json");
                            var content = new StringContent("\r\n{\r\n  \"amount\": " + Decimal.Round(finalamount) + ",\r\n  \"currency\": \"" + getsubinfo.Currency + "\",\r\n  \"customer_initiated\": false,\r\n  \"threeDSecure\": true,\r\n  \"save_card\": false,\r\n  \"payment_agreement\": {\r\n    \"contract\": {\r\n      \"id\": \"" + getuserinfo.Tap_Card_ID + "\"\r\n    },\r\n    \"id\": \"" + getuserinfo.Tap_Agreement_ID + "\"\r\n  },\r\n  \"receipt\": {\r\n    \"email\": true,\r\n    \"sms\": true\r\n  },\"reference\": {\r\n    \"transaction\": \"" + TransNo + "\",\r\n    \"order\": \"" + OrderNo + "\"\r\n  },\r\n  \"customer\": {\r\n    \"id\": \"" + getuserinfo.Tap_CustomerID + "\"\r\n  },\r\n  \"merchant\": {\r\n    \"id\": \"22116401\"\r\n  },\r\n  \"source\": {\r\n    \"id\": \"" + Deserialized_savecard.id + "\"\r\n  },\r\n  \"redirect\": {\r\n    \"url\": \"https://1f3b186efe31e8696c144578816c5443.m.pipedream.net/\"\r\n  }\r\n}\r\n", null, "application/json");
                            request.Content = content;
                            var response = await client.SendAsync(request);
                            var bodys = await response.Content.ReadAsStringAsync();
                            CreateCharge deserialized_CreateCharge = JsonConvert.DeserializeObject<CreateCharge>(bodys);
                            if (deserialized_CreateCharge.status == "CAPTURED")
                            {
                                Invoice invoice = new Invoice
                                {
                                    InvoiceStartDate = DateTime.Now,
                                    InvoiceEndDate = DateTime.Now.AddDays(7),
                                    AddedDate = DateTime.Now,
                                    AddedBy = getuserinfo.FullName,
                                    SubscriptionAmount = finalamount,
                                    SubscriptionId = getsubinfo.SubscriptionId,
                                    Status = "Payment Captured",
                                    IsDeleted = false,
                                    Description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")",
                                    SubscriptionName = getsubinfo.Name,
                                    UserId = getuserinfo.Id,
                                    ChargeId = deserialized_CreateCharge.id,
                                };
                                _context.invoices.Add(invoice);
                                _context.SaveChanges();
                                //Next Recurrening Job Date
                                RecurringCharge recurringCharge = new RecurringCharge();
                                recurringCharge.Amount = Convert.ToDecimal(item.Amount);
                                recurringCharge.SubscriptionId = getsubinfo.SubscriptionId;
                                recurringCharge.UserID = getuserinfo.Id;
                                recurringCharge.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                recurringCharge.ChargeId = deserialized_CreateCharge.id;
                                recurringCharge.JobRunDate = invoice.InvoiceEndDate.AddDays(1);
                                _context.recurringCharges.Add(recurringCharge);
                                _context.SaveChanges();
                            }
                        }
                        else if (getuserinfo.Frequency == "MONTHLY")
                        {
                            var calculate_amount = getsubinfo.Amount;
                            int finalamount = Convert.ToInt32(calculate_amount) / 1;
                            // Create a charge
                            var client = new HttpClient();
                            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tap.company/v2/charges");
                            request.Headers.Add("Authorization", "Bearer " + getuserinfo.SecertKey);
                            request.Headers.Add("accept", "application/json");
                            var content = new StringContent("\r\n{\r\n  \"amount\": " + Decimal.Round(finalamount) + ",\r\n  \"currency\": \"" + getsubinfo.Currency + "\",\r\n  \"customer_initiated\": false,\r\n  \"threeDSecure\": true,\r\n  \"save_card\": false,\r\n  \"payment_agreement\": {\r\n    \"contract\": {\r\n      \"id\": \"" + getuserinfo.Tap_Card_ID + "\"\r\n    },\r\n    \"id\": \"" + getuserinfo.Tap_Agreement_ID + "\"\r\n  },\r\n  \"receipt\": {\r\n    \"email\": true,\r\n    \"sms\": true\r\n  },\"reference\": {\r\n    \"transaction\": \"" + TransNo + "\",\r\n    \"order\": \"" + OrderNo + "\"\r\n  },\r\n  \"customer\": {\r\n    \"id\": \"" + getuserinfo.Tap_CustomerID + "\"\r\n  },\r\n  \"merchant\": {\r\n    \"id\": \"22116401\"\r\n  },\r\n  \"source\": {\r\n    \"id\": \"" + Deserialized_savecard.id + "\"\r\n  },\r\n  \"redirect\": {\r\n    \"url\": \"https://1f3b186efe31e8696c144578816c5443.m.pipedream.net/\"\r\n  }\r\n}\r\n", null, "application/json");
                            request.Content = content;
                            var response = await client.SendAsync(request);
                            var bodys = await response.Content.ReadAsStringAsync();
                            CreateCharge deserialized_CreateCharge = JsonConvert.DeserializeObject<CreateCharge>(bodys);
                            if (deserialized_CreateCharge.status == "CAPTURED")
                            {
                                Invoice invoice = new Invoice
                                {
                                    InvoiceStartDate = DateTime.Now,
                                    InvoiceEndDate = DateTime.Now.AddMonths(1),
                                    AddedDate = DateTime.Now,
                                    AddedBy = getuserinfo.FullName,
                                    SubscriptionAmount = finalamount,
                                    SubscriptionId = getsubinfo.SubscriptionId,
                                    Status = "Payment Captured",
                                    IsDeleted = false,
                                    Description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")",
                                    SubscriptionName = getsubinfo.Name,
                                    UserId = getuserinfo.Id,
                                    ChargeId = deserialized_CreateCharge.id,
                                };
                                _context.invoices.Add(invoice);
                                _context.SaveChanges();
                                //Next Recurrening Job Date
                                RecurringCharge recurringCharge = new RecurringCharge();
                                recurringCharge.Amount = Convert.ToDecimal(item.Amount);
                                recurringCharge.SubscriptionId = getsubinfo.SubscriptionId;
                                recurringCharge.UserID = getuserinfo.Id;
                                recurringCharge.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                recurringCharge.ChargeId = deserialized_CreateCharge.id;
                                recurringCharge.JobRunDate = invoice.InvoiceEndDate.AddDays(1);
                                _context.recurringCharges.Add(recurringCharge);
                                _context.SaveChanges();
                            }
                        }
                        else if (getuserinfo.Frequency == "QUARTERLY")
                        {
                            var calculate_amount = getsubinfo.Amount;
                            int finalamount = (Convert.ToInt32(calculate_amount) * 3) / 1;
                            // Create a charge
                            var client = new HttpClient();
                            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tap.company/v2/charges");
                            request.Headers.Add("Authorization", "Bearer " + getuserinfo.SecertKey);
                            request.Headers.Add("accept", "application/json");
                            var content = new StringContent("\r\n{\r\n  \"amount\": " + Decimal.Round(finalamount) + ",\r\n  \"currency\": \"" + getsubinfo.Currency + "\",\r\n  \"customer_initiated\": false,\r\n  \"threeDSecure\": true,\r\n  \"save_card\": false,\r\n  \"payment_agreement\": {\r\n    \"contract\": {\r\n      \"id\": \"" + getuserinfo.Tap_Card_ID + "\"\r\n    },\r\n    \"id\": \"" + getuserinfo.Tap_Agreement_ID + "\"\r\n  },\r\n  \"receipt\": {\r\n    \"email\": true,\r\n    \"sms\": true\r\n  },\"reference\": {\r\n    \"transaction\": \"" + TransNo + "\",\r\n    \"order\": \"" + OrderNo + "\"\r\n  },\r\n  \"customer\": {\r\n    \"id\": \"" + getuserinfo.Tap_CustomerID + "\"\r\n  },\r\n  \"merchant\": {\r\n    \"id\": \"22116401\"\r\n  },\r\n  \"source\": {\r\n    \"id\": \"" + Deserialized_savecard.id + "\"\r\n  },\r\n  \"redirect\": {\r\n    \"url\": \"https://1f3b186efe31e8696c144578816c5443.m.pipedream.net/\"\r\n  }\r\n}\r\n", null, "application/json");
                            request.Content = content;
                            var response = await client.SendAsync(request);
                            var bodys = await response.Content.ReadAsStringAsync();
                            CreateCharge deserialized_CreateCharge = JsonConvert.DeserializeObject<CreateCharge>(bodys);
                            if (deserialized_CreateCharge.status == "CAPTURED")
                            {
                                Invoice invoice = new Invoice
                                {
                                    InvoiceStartDate = DateTime.Now,
                                    InvoiceEndDate = DateTime.Now.AddMonths(3),
                                    AddedDate = DateTime.Now,
                                    AddedBy = getuserinfo.FullName,
                                    SubscriptionAmount = finalamount,
                                    SubscriptionId = getsubinfo.SubscriptionId,
                                    Status = "Payment Captured",
                                    IsDeleted = false,
                                    Description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")",
                                    SubscriptionName = getsubinfo.Name,
                                    UserId = getuserinfo.Id,
                                    ChargeId = deserialized_CreateCharge.id,
                                };
                                _context.invoices.Add(invoice);
                                _context.SaveChanges();
                                //Next Recurrening Job Date
                                RecurringCharge recurringCharge = new RecurringCharge();
                                recurringCharge.Amount = Convert.ToDecimal(item.Amount);
                                recurringCharge.SubscriptionId = getsubinfo.SubscriptionId;
                                recurringCharge.UserID = getuserinfo.Id;
                                recurringCharge.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                recurringCharge.ChargeId = deserialized_CreateCharge.id;
                                recurringCharge.JobRunDate = invoice.InvoiceEndDate.AddDays(1);
                                _context.recurringCharges.Add(recurringCharge);
                                _context.SaveChanges();
                            }
                        }
                        else if (getuserinfo.Frequency == "HALFYEARLY")
                        {
                            var calculate_amount = getsubinfo.Amount;
                            int finalamount = (Convert.ToInt32(calculate_amount) * 6) / 1;
                            // Create a charge
                            var client = new HttpClient();
                            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tap.company/v2/charges");
                            request.Headers.Add("Authorization", "Bearer " + getuserinfo.SecertKey);
                            request.Headers.Add("accept", "application/json");
                            var content = new StringContent("\r\n{\r\n  \"amount\": " + Decimal.Round(finalamount) + ",\r\n  \"currency\": \"" + getsubinfo.Currency + "\",\r\n  \"customer_initiated\": false,\r\n  \"threeDSecure\": true,\r\n  \"save_card\": false,\r\n  \"payment_agreement\": {\r\n    \"contract\": {\r\n      \"id\": \"" + getuserinfo.Tap_Card_ID + "\"\r\n    },\r\n    \"id\": \"" + getuserinfo.Tap_Agreement_ID + "\"\r\n  },\r\n  \"receipt\": {\r\n    \"email\": true,\r\n    \"sms\": true\r\n  },\"reference\": {\r\n    \"transaction\": \"" + TransNo + "\",\r\n    \"order\": \"" + OrderNo + "\"\r\n  },\r\n  \"customer\": {\r\n    \"id\": \"" + getuserinfo.Tap_CustomerID + "\"\r\n  },\r\n  \"merchant\": {\r\n    \"id\": \"22116401\"\r\n  },\r\n  \"source\": {\r\n    \"id\": \"" + Deserialized_savecard.id + "\"\r\n  },\r\n  \"redirect\": {\r\n    \"url\": \"https://1f3b186efe31e8696c144578816c5443.m.pipedream.net/\"\r\n  }\r\n}\r\n", null, "application/json");
                            request.Content = content;
                            var response = await client.SendAsync(request);
                            var bodys = await response.Content.ReadAsStringAsync();
                            CreateCharge deserialized_CreateCharge = JsonConvert.DeserializeObject<CreateCharge>(bodys);
                            if (deserialized_CreateCharge.status == "CAPTURED")
                            {
                                Invoice invoice = new Invoice
                                {
                                    InvoiceStartDate = DateTime.Now,
                                    InvoiceEndDate = DateTime.Now.AddMonths(6),
                                    AddedDate = DateTime.Now,
                                    AddedBy = getuserinfo.FullName,
                                    SubscriptionAmount = finalamount,
                                    SubscriptionId = getsubinfo.SubscriptionId,
                                    Status = "Payment Captured",
                                    IsDeleted = false,
                                    Description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")",
                                    SubscriptionName = getsubinfo.Name,
                                    UserId = getuserinfo.Id,
                                    ChargeId = deserialized_CreateCharge.id,
                                };
                                _context.invoices.Add(invoice);
                                _context.SaveChanges();
                                //Next Recurrening Job Date
                                RecurringCharge recurringCharge = new RecurringCharge();
                                recurringCharge.Amount = Convert.ToDecimal(item.Amount);
                                recurringCharge.SubscriptionId = getsubinfo.SubscriptionId;
                                recurringCharge.UserID = getuserinfo.Id;
                                recurringCharge.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                recurringCharge.ChargeId = deserialized_CreateCharge.id;
                                recurringCharge.JobRunDate = invoice.InvoiceEndDate.AddDays(1);
                                _context.recurringCharges.Add(recurringCharge);
                                _context.SaveChanges();
                            }
                        }
                        else if (getuserinfo.Frequency == "YEARLY")
                        {
                            var calculate_amount = getsubinfo.Amount;
                            var discount_amount = (Convert.ToInt32(calculate_amount) / 100) * 10;
                            int finalamount = (Convert.ToInt32(calculate_amount) - discount_amount) * 12;
                            // Create a charge
                            var client = new HttpClient();
                            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tap.company/v2/charges");
                            request.Headers.Add("Authorization", "Bearer " + getuserinfo.SecertKey);
                            request.Headers.Add("accept", "application/json");
                            var content = new StringContent("\r\n{\r\n  \"amount\": " + Decimal.Round(finalamount) + ",\r\n  \"currency\": \"" + getsubinfo.Currency + "\",\r\n  \"customer_initiated\": false,\r\n  \"threeDSecure\": true,\r\n  \"save_card\": false,\r\n  \"payment_agreement\": {\r\n    \"contract\": {\r\n      \"id\": \"" + getuserinfo.Tap_Card_ID + "\"\r\n    },\r\n    \"id\": \"" + getuserinfo.Tap_Agreement_ID + "\"\r\n  },\r\n  \"receipt\": {\r\n    \"email\": true,\r\n    \"sms\": true\r\n  },\"reference\": {\r\n    \"transaction\": \"" + TransNo + "\",\r\n    \"order\": \"" + OrderNo + "\"\r\n  },\r\n  \"customer\": {\r\n    \"id\": \"" + getuserinfo.Tap_CustomerID + "\"\r\n  },\r\n  \"merchant\": {\r\n    \"id\": \"22116401\"\r\n  },\r\n  \"source\": {\r\n    \"id\": \"" + Deserialized_savecard.id + "\"\r\n  },\r\n  \"redirect\": {\r\n    \"url\": \"https://1f3b186efe31e8696c144578816c5443.m.pipedream.net/\"\r\n  }\r\n}\r\n", null, "application/json");
                            request.Content = content;
                            var response = await client.SendAsync(request);
                            var bodys = await response.Content.ReadAsStringAsync();
                            CreateCharge deserialized_CreateCharge = JsonConvert.DeserializeObject<CreateCharge>(bodys);
                            if (deserialized_CreateCharge.status == "CAPTURED")
                            {
                                Invoice invoice = new Invoice
                                {
                                    InvoiceStartDate = DateTime.Now,
                                    InvoiceEndDate = DateTime.Now.AddMonths(12),
                                    AddedDate = DateTime.Now,
                                    AddedBy = getuserinfo.FullName,
                                    SubscriptionAmount = finalamount,
                                    SubscriptionId = getsubinfo.SubscriptionId,
                                    Status = "Payment Captured",
                                    IsDeleted = false,
                                    Description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")",
                                    SubscriptionName = getsubinfo.Name,
                                    UserId = getuserinfo.Id,
                                    ChargeId = deserialized_CreateCharge.id,
                                };
                                _context.invoices.Add(invoice);
                                _context.SaveChanges();
                                //Next Recurrening Job Date
                                RecurringCharge recurringCharge = new RecurringCharge();
                                recurringCharge.Amount = Convert.ToDecimal(item.Amount);
                                recurringCharge.SubscriptionId = getsubinfo.SubscriptionId;
                                recurringCharge.UserID = getuserinfo.Id;
                                recurringCharge.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                recurringCharge.ChargeId = deserialized_CreateCharge.id;
                                recurringCharge.JobRunDate = invoice.InvoiceEndDate.AddDays(1);
                                _context.recurringCharges.Add(recurringCharge);
                                _context.SaveChanges();
                            }
                        }
                        // Update Job Table
                        var recurreningjob = _context.recurringCharges.Where(x => x.RecurringChargeId == item.RecurringChargeId).FirstOrDefault();
                        recurreningjob.IsRun = true;
                        recurreningjob.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                        _context.recurringCharges.Update(recurreningjob);
                        _context.SaveChanges();
                        //Send Email
                        // Send Email
                        string body = string.Empty;
                        _environment.WebRootPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                        string contentRootPath = _environment.WebRootPath + "/htmltopdf.html";
                        string contentRootPath1 = _environment.WebRootPath + "/css/bootstrap.min.css";
                        //Generate PDF
                        using (StreamReader reader = new StreamReader(contentRootPath))
                        {
                            body = reader.ReadToEnd();
                        }
                        //Fill EMail By Parameter
                        body = body.Replace("{title}", "Tamarran Payment Invoice");
                        body = body.Replace("{currentdate}", DateTime.Now.ToString());

                        body = body.Replace("{InvocieStatus}", "Recurrening Payment Captured");
                        body = body.Replace("{InvoiceID}", "Inv" + "233");
                        body = body.Replace("{InvoiceAmount}", "23");


                        body = body.Replace("{User_Name}", getuserinfo.FullName);
                        body = body.Replace("{User_Email}", getuserinfo.Email);
                        body = body.Replace("{User_Country}", getuserinfo.Country);
                        body = body.Replace("{User_Phone}", getuserinfo.PhoneNumber);


                        body = body.Replace("{SubscriptionName}", getsubinfo.Name);
                        body = body.Replace("{SubscriptionPeriod}", getsubinfo.Frequency);
                        body = body.Replace("{VAT}", "0.00");
                        body = body.Replace("{SetupFee}", "0.00");
                        int amount = Convert.ToInt32(getsubinfo.Amount);
                        body = body.Replace("{Total}", amount.ToString());
                        body = body.Replace("{SubscriptionAmount}", getsubinfo.Amount);

                        var renderer = new ChromePdfRenderer();
                        // Many rendering options to use to customize!
                        renderer.RenderingOptions.SetCustomPaperSizeInInches(6.9, 12);
                        renderer.RenderingOptions.PaperOrientation = IronPdf.Rendering.PdfPaperOrientation.Portrait;
                        renderer.RenderingOptions.Title = "Recurrening Payment";
                        renderer.RenderingOptions.EnableJavaScript = true;
                        renderer.RenderingOptions.Zoom = 100;

                        // Supports margin customization!
                        renderer.RenderingOptions.MarginTop = 0; //millimeters
                        renderer.RenderingOptions.MarginLeft = 0; //millimeters
                        renderer.RenderingOptions.MarginRight = 0; //millimeters
                        renderer.RenderingOptions.MarginBottom = 0; //millimeters

                        // Can set FirstPageNumber if you have a cover page
                        renderer.RenderingOptions.FirstPageNumber = 1;

                        // Settings have been set, we can render:
                        var pdf = renderer.RenderHtmlAsPdf(body);
                        pdf.SaveAs("TamrranInvoice.pdf");


                        string pdfpath = _environment.ContentRootPath + "/TamrranInvoice.pdf";
                        byte[] bytes = System.IO.File.ReadAllBytes(pdfpath);

                        _ = _emailSender.SendEmailWithFIle(bytes, getuserinfo.Email, "Recurring Recurrening Payment Captured", "Your Recurring Payment has been received successfully. Thank you.");
                    }
                }
            }

        }
    }
}
