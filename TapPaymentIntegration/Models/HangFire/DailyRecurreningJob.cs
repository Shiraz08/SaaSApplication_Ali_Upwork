using Microsoft.AspNetCore.Identity;
using Newtonsoft.Json;
using TapPaymentIntegration.Areas.Identity.Data;
using TapPaymentIntegration.Controllers;
using TapPaymentIntegration.Data;
using TapPaymentIntegration.Migrations;
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
         public readonly string RedirectURL = "https://tappayment.niralahyderabadirestaurant.com";
        //public readonly string RedirectURL = "https://localhost:7279";
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
            CreateCharge deserialized_CreateCharge = null;
            var recurringCharges_list = _context.recurringCharges.Where(x => x.JobRunDate.Date == DateTime.Now.Date && x.IsRun == false).ToList();
           //var recurringCharges_list = _context.recurringCharges.Where(x => x.JobRunDate.Date == DateTime.Now.AddDays(1).Date && x.IsRun == false).ToList();
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
                        decimal finalamount = 0;
                        decimal Discount = 0;
                        decimal Vat = 0;
                        decimal sun_amount = 0;
                        if (getuserinfo.Frequency == "DAILY")
                        {
                            Discount = 0;
                            finalamount = (decimal)Convert.ToInt32(getsubinfo.Amount) / (int)days;
                        }
                        else if (getuserinfo.Frequency == "WEEKLY")
                        {
                            Discount = 0;
                            finalamount = (decimal)Convert.ToInt32(getsubinfo.Amount) / 4;
                        }
                        else if (getuserinfo.Frequency == "MONTHLY")
                        {
                            Discount = 0;
                            finalamount = (decimal)Convert.ToInt32(getsubinfo.Amount) / 1;
                        }
                        else if (getuserinfo.Frequency == "QUARTERLY")
                        {
                            Discount = 0;
                            finalamount = (decimal)(Convert.ToInt32(getsubinfo.Amount) * 3) / 1;
                        }
                        else if (getuserinfo.Frequency == "HALFYEARLY")
                        {
                            Discount = 0;
                            finalamount = (decimal)(Convert.ToInt32(getsubinfo.Amount) * 6) / 1;
                        }
                        else if (getuserinfo.Frequency == "YEARLY")
                        {
                            var amountpercentage = (decimal)(Convert.ToInt32(getsubinfo.Amount) / 100) * 10;
                            var final_amount_percentage = Convert.ToInt32(getsubinfo.Amount) - amountpercentage;
                            finalamount = final_amount_percentage * 12;
                            Discount = amountpercentage * 12;
                        }
                        if (getsubinfo.VAT == null)
                        {
                            Vat = 0;
                        }
                        else
                        {
                            decimal totala = finalamount;
                            sun_amount = totala;
                            Vat = (decimal)((totala / Convert.ToInt32(getsubinfo.VAT)) * 100) / 100;
                        }
                        decimal after_vat_totalamount = finalamount + Vat;
                        if (getuserinfo.Frequency == "DAILY")
                        {
                            // Create a charge
                            var client = new HttpClient();
                            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tap.company/v2/charges");
                            request.Headers.Add("Authorization", "Bearer " + getuserinfo.SecertKey);
                            request.Headers.Add("accept", "application/json");
                            var content = new StringContent("\r\n{\r\n  \"amount\": " + Decimal.Round(after_vat_totalamount) + ",\r\n  \"currency\": \"" + getsubinfo.Currency + "\",\r\n  \"customer_initiated\": false,\r\n  \"threeDSecure\": true,\r\n  \"save_card\": false,\r\n  \"payment_agreement\": {\r\n    \"contract\": {\r\n      \"id\": \"" + getuserinfo.Tap_Card_ID + "\"\r\n    },\r\n    \"id\": \"" + getuserinfo.Tap_Agreement_ID + "\"\r\n  },\r\n  \"receipt\": {\r\n    \"email\": true,\r\n    \"sms\": true\r\n  },\"reference\": {\r\n    \"transaction\": \"" + TransNo + "\",\r\n    \"order\": \"" + OrderNo + "\"\r\n  },\r\n  \"customer\": {\r\n    \"id\": \"" + getuserinfo.Tap_CustomerID + "\"\r\n  },\r\n  \"source\": {\r\n    \"id\": \"" + Deserialized_savecard.id + "\"\r\n  },\r\n  \"redirect\": {\r\n    \"url\": \"https://test.com/\"\r\n  }\r\n}\r\n", null, "application/json");
                            request.Content = content;
                            var response = await client.SendAsync(request);
                            var bodys = await response.Content.ReadAsStringAsync();
                            deserialized_CreateCharge = JsonConvert.DeserializeObject<CreateCharge>(bodys);
                            if (deserialized_CreateCharge.status == "CAPTURED")
                            {
                                Invoice invoice = new Invoice
                                {
                                    InvoiceStartDate = DateTime.Now,
                                    InvoiceEndDate = DateTime.Now.AddDays(1),
                                    AddedDate = DateTime.Now,
                                    AddedBy = getuserinfo.FullName,
                                    SubscriptionAmount = Convert.ToInt32(decimal.Round(after_vat_totalamount)),
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


                                // Update Job Table
                                var recurreningjob = _context.recurringCharges.Where(x => x.RecurringChargeId == item.RecurringChargeId).FirstOrDefault();
                                recurreningjob.IsRun = true;
                                recurreningjob.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                _context.recurringCharges.Update(recurreningjob);
                                _context.SaveChanges();
                                //Send Email
                                int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
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
                                body = body.Replace("{currentdate}", DateTime.Now.ToString());

                                body = body.Replace("{InvocieStatus}", "Payment Captured");
                                body = body.Replace("{InvoiceID}", "Inv" + max_invoice_id);


                                body = body.Replace("{User_Name}", getuserinfo.FullName);
                                body = body.Replace("{User_Email}", getuserinfo.Email);
                                body = body.Replace("{User_GYM}", getuserinfo.GYMName);
                                body = body.Replace("{User_Phone}", getuserinfo.PhoneNumber);

                                body = body.Replace("{SubscriptionName}", getsubinfo.Name);
                                body = body.Replace("{Discount}", Discount.ToString());
                                body = body.Replace("{SubscriptionPeriod}", getuserinfo.Frequency);
                                body = body.Replace("{SetupFee}", getsubinfo.SetupFee + " " + getsubinfo.Currency);
                                int amount = Convert.ToInt32(incoice_info.SubscriptionAmount);
                                body = body.Replace("{SubscriptionAmount}", decimal.Round(sun_amount, 2).ToString() + " " + getsubinfo.Currency);
                                //Calculate VAT
                                if (getsubinfo.VAT == null)
                                {
                                    body = body.Replace("{VAT}", "0.00");
                                    body = body.Replace("{Total}", amount.ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{InvoiceAmount}", amount.ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(amount), 2).ToString() + " " + getsubinfo.Currency);
                                }
                                else
                                {
                                    body = body.Replace("{VAT}", decimal.Round(Convert.ToDecimal(Vat), 2).ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{Total}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{InvoiceAmount}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(amount), 2).ToString() + " " + getsubinfo.Currency);
                                }
                                var bytes = (new NReco.PdfGenerator.HtmlToPdfConverter()).GeneratePdf(body);
                                _ = _emailSender.SendEmailWithFIle(bytes, getuserinfo.Email, "Tamarran - Automatic Payment Confirmation", "Hello " + getuserinfo.GYMName + ",<br />Kindly note that your subscription payment to Tamarran was made successfully.<br />Attached is the receipt for subscription auto-payment transaction.<br />Thank you for your business.<br />Tamarran's team!");
                            }
                            else
                            {
                                Invoice invoice = new Invoice
                                {
                                    InvoiceStartDate = DateTime.Now,
                                    InvoiceEndDate = DateTime.Now.AddDays(1),
                                    AddedDate = DateTime.Now,
                                    AddedBy = getuserinfo.FullName,
                                    SubscriptionAmount = Convert.ToInt32(decimal.Round(after_vat_totalamount)),
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
                                };
                                _context.invoices.Add(invoice);
                                _context.SaveChanges();
                                int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
                                var nameinvoice = "Inv" + max_invoice_id;
                                _ = _emailSender.SendEmailAsync(getuserinfo.Email, "Tamarran - Your subscription renewal in Tamarran failed", "Hello " + getuserinfo.GYMName + ",<br /><br />On " + DateTime.Now.ToShortDateString() + ", the payment for " + nameinvoice + " failed.<br /><br />Update your card details via: " + RedirectURL + " <br /><br /><br />Please update your credit or debit card details through the above link as soon as possible to complete the payment.<br /><br />If you have any questions, please e-mail us at accounts@tamarran.com or call us on +973-36021122 or +966-557070136.<br /><br />Thanks,<br /><br />Tamarran Team");
                            }
                        }
                        else if (getuserinfo.Frequency == "WEEKLY")
                        {
                            // Create a charge
                            var client = new HttpClient();
                            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tap.company/v2/charges");
                            request.Headers.Add("Authorization", "Bearer " + getuserinfo.SecertKey);
                            request.Headers.Add("accept", "application/json");
                            var content = new StringContent("\r\n{\r\n  \"amount\": " + Decimal.Round(after_vat_totalamount) + ",\r\n  \"currency\": \"" + getsubinfo.Currency + "\",\r\n  \"customer_initiated\": false,\r\n  \"threeDSecure\": true,\r\n  \"save_card\": false,\r\n  \"payment_agreement\": {\r\n    \"contract\": {\r\n      \"id\": \"" + getuserinfo.Tap_Card_ID + "\"\r\n    },\r\n    \"id\": \"" + getuserinfo.Tap_Agreement_ID + "\"\r\n  },\r\n  \"receipt\": {\r\n    \"email\": true,\r\n    \"sms\": true\r\n  },\"reference\": {\r\n    \"transaction\": \"" + TransNo + "\",\r\n    \"order\": \"" + OrderNo + "\"\r\n  },\r\n  \"customer\": {\r\n    \"id\": \"" + getuserinfo.Tap_CustomerID + "\"\r\n  },\r\n  \"source\": {\r\n    \"id\": \"" + Deserialized_savecard.id + "\"\r\n  },\r\n  \"redirect\": {\r\n    \"url\": \"https://1f3b186efe31e8696c144578816c5443.m.pipedream.net/\"\r\n  }\r\n}\r\n", null, "application/json");
                            request.Content = content;
                            var response = await client.SendAsync(request);
                            var bodys = await response.Content.ReadAsStringAsync();
                            deserialized_CreateCharge = JsonConvert.DeserializeObject<CreateCharge>(bodys);
                            if (deserialized_CreateCharge.status == "CAPTURED")
                            {
                                Invoice invoice = new Invoice
                                {
                                    InvoiceStartDate = DateTime.Now,
                                    InvoiceEndDate = DateTime.Now.AddDays(7),
                                    Currency = getsubinfo.Currency,
                                    AddedDate = DateTime.Now,
                                    AddedBy = getuserinfo.FullName,
                                    SubscriptionAmount = Convert.ToInt32(decimal.Round(after_vat_totalamount)),
                                    VAT = Vat.ToString(),
                                    Discount = Discount.ToString(),
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

                                // Update Job Table
                                var recurreningjob = _context.recurringCharges.Where(x => x.RecurringChargeId == item.RecurringChargeId).FirstOrDefault();
                                recurreningjob.IsRun = true;
                                recurreningjob.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                _context.recurringCharges.Update(recurreningjob);
                                _context.SaveChanges();
                                //Send Email
                                int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
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
                                body = body.Replace("{currentdate}", DateTime.Now.ToString());

                                body = body.Replace("{InvocieStatus}", "Payment Captured");
                                body = body.Replace("{InvoiceID}", "Inv" + max_invoice_id);


                                body = body.Replace("{User_Name}", getuserinfo.FullName);
                                body = body.Replace("{User_Email}", getuserinfo.Email);
                                body = body.Replace("{User_GYM}", getuserinfo.GYMName);
                                body = body.Replace("{User_Phone}", getuserinfo.PhoneNumber);

                                body = body.Replace("{SubscriptionName}", getsubinfo.Name);
                                body = body.Replace("{Discount}", Discount.ToString());
                                body = body.Replace("{SubscriptionPeriod}", getuserinfo.Frequency);
                                body = body.Replace("{SetupFee}", getsubinfo.SetupFee + " " + getsubinfo.Currency);
                                int amount = Convert.ToInt32(incoice_info.SubscriptionAmount);
                                body = body.Replace("{SubscriptionAmount}", decimal.Round(sun_amount, 2).ToString() + " " + getsubinfo.Currency);
                                //Calculate VAT
                                if (getsubinfo.VAT == null)
                                {
                                    body = body.Replace("{VAT}", "0.00");
                                    body = body.Replace("{Total}", amount.ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{InvoiceAmount}", amount.ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(amount), 2).ToString() + " " + getsubinfo.Currency);
                                }
                                else
                                {
                                    body = body.Replace("{VAT}", decimal.Round(Convert.ToDecimal(Vat), 2).ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{Total}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{InvoiceAmount}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(amount), 2).ToString() + " " + getsubinfo.Currency);
                                }
                                var bytes = (new NReco.PdfGenerator.HtmlToPdfConverter()).GeneratePdf(body);
                                _ = _emailSender.SendEmailWithFIle(bytes, getuserinfo.Email, "Tamarran - Automatic Payment Confirmation", "Hello " + getuserinfo.GYMName + ",<br />Kindly note that your subscription payment to Tamarran was made successfully.<br />Attached is the receipt for subscription auto-payment transaction.<br />Thank you for your business.<br />Tamarran's team!");
                            }
                            else
                            {
                                Invoice invoice = new Invoice
                                {
                                    InvoiceStartDate = DateTime.Now,
                                    InvoiceEndDate = DateTime.Now.AddDays(7),
                                    Currency = getsubinfo.Currency,
                                    AddedDate = DateTime.Now,
                                    AddedBy = getuserinfo.FullName,
                                    SubscriptionAmount = Convert.ToInt32(decimal.Round(after_vat_totalamount)),
                                    VAT = Vat.ToString(),
                                    Discount = Discount.ToString(),
                                    SubscriptionId = getsubinfo.SubscriptionId,
                                    Status = "Un-Paid",
                                    IsDeleted = false,
                                    Description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")",
                                    SubscriptionName = getsubinfo.Name,
                                    UserId = getuserinfo.Id,
                                    ChargeId = deserialized_CreateCharge.id,
                                };
                                _context.invoices.Add(invoice);
                                _context.SaveChanges();
                                int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
                                var nameinvoice = "Inv" + max_invoice_id;
                                _ = _emailSender.SendEmailAsync(getuserinfo.Email, "Tamarran - Your subscription renewal in Tamarran failed", "Hello " + getuserinfo.GYMName + ",<br /><br />On " + DateTime.Now.ToShortDateString() + ", the payment for " + nameinvoice + " failed.<br /><br />Update your card details via: " + RedirectURL + " <br /><br /><br />Please update your credit or debit card details through the above link as soon as possible to complete the payment.<br /><br />If you have any questions, please e-mail us at accounts@tamarran.com or call us on +973-36021122 or +966-557070136.<br /><br />Thanks,<br /><br />Tamarran Team");
                            }
                        }
                        else if (getuserinfo.Frequency == "MONTHLY")
                        {
                            // Create a charge
                            var client = new HttpClient();
                            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tap.company/v2/charges");
                            request.Headers.Add("Authorization", "Bearer " + getuserinfo.SecertKey);
                            request.Headers.Add("accept", "application/json");
                            var content = new StringContent("\r\n{\r\n  \"amount\": " + Decimal.Round(after_vat_totalamount) + ",\r\n  \"currency\": \"" + getsubinfo.Currency + "\",\r\n  \"customer_initiated\": false,\r\n  \"threeDSecure\": true,\r\n  \"save_card\": false,\r\n  \"payment_agreement\": {\r\n    \"contract\": {\r\n      \"id\": \"" + getuserinfo.Tap_Card_ID + "\"\r\n    },\r\n    \"id\": \"" + getuserinfo.Tap_Agreement_ID + "\"\r\n  },\r\n  \"receipt\": {\r\n    \"email\": true,\r\n    \"sms\": true\r\n  },\"reference\": {\r\n    \"transaction\": \"" + TransNo + "\",\r\n    \"order\": \"" + OrderNo + "\"\r\n  },\r\n  \"customer\": {\r\n    \"id\": \"" + getuserinfo.Tap_CustomerID + "\"\r\n  },\r\n  \"source\": {\r\n    \"id\": \"" + Deserialized_savecard.id + "\"\r\n  },\r\n  \"redirect\": {\r\n    \"url\": \"https://1f3b186efe31e8696c144578816c5443.m.pipedream.net/\"\r\n  }\r\n}\r\n", null, "application/json");
                            request.Content = content;
                            var response = await client.SendAsync(request);
                            var bodys = await response.Content.ReadAsStringAsync();
                            deserialized_CreateCharge = JsonConvert.DeserializeObject<CreateCharge>(bodys);
                            if (deserialized_CreateCharge.status == "CAPTURED")
                            {
                                Invoice invoice = new Invoice
                                {
                                    InvoiceStartDate = DateTime.Now,
                                    InvoiceEndDate = DateTime.Now.AddMonths(1),
                                    AddedDate = DateTime.Now,
                                    AddedBy = getuserinfo.FullName,
                                    SubscriptionAmount = Convert.ToInt32(decimal.Round(after_vat_totalamount)),
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

                                // Update Job Table
                                var recurreningjob = _context.recurringCharges.Where(x => x.RecurringChargeId == item.RecurringChargeId).FirstOrDefault();
                                recurreningjob.IsRun = true;
                                recurreningjob.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                _context.recurringCharges.Update(recurreningjob);
                                _context.SaveChanges();
                                //Send Email
                                int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
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
                                body = body.Replace("{currentdate}", DateTime.Now.ToString());

                                body = body.Replace("{InvocieStatus}", "Payment Captured");
                                body = body.Replace("{InvoiceID}", "Inv" + max_invoice_id);


                                body = body.Replace("{User_Name}", getuserinfo.FullName);
                                body = body.Replace("{User_Email}", getuserinfo.Email);
                                body = body.Replace("{User_GYM}", getuserinfo.GYMName);
                                body = body.Replace("{User_Phone}", getuserinfo.PhoneNumber);

                                body = body.Replace("{SubscriptionName}", getsubinfo.Name);
                                body = body.Replace("{Discount}", Discount.ToString());
                                body = body.Replace("{SubscriptionPeriod}", getuserinfo.Frequency);
                                body = body.Replace("{SetupFee}", getsubinfo.SetupFee + " " + getsubinfo.Currency);
                                int amount = Convert.ToInt32(incoice_info.SubscriptionAmount);
                                body = body.Replace("{SubscriptionAmount}", decimal.Round(sun_amount, 2).ToString() + " " + getsubinfo.Currency);
                                //Calculate VAT
                                if (getsubinfo.VAT == null)
                                {
                                    body = body.Replace("{VAT}", "0.00");
                                    body = body.Replace("{Total}", amount.ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{InvoiceAmount}", amount.ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(amount), 2).ToString() + " " + getsubinfo.Currency);
                                }
                                else
                                {
                                    body = body.Replace("{VAT}", decimal.Round(Convert.ToDecimal(Vat), 2).ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{Total}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{InvoiceAmount}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(amount), 2).ToString() + " " + getsubinfo.Currency);
                                }
                                var bytes = (new NReco.PdfGenerator.HtmlToPdfConverter()).GeneratePdf(body);
                                _ = _emailSender.SendEmailWithFIle(bytes, getuserinfo.Email, "Tamarran - Automatic Payment Confirmation", "Hello " + getuserinfo.GYMName + ",<br />Kindly note that your subscription payment to Tamarran was made successfully.<br />Attached is the receipt for subscription auto-payment transaction.<br />Thank you for your business.<br />Tamarran's team!");
                            }
                            else
                            {
                                Invoice invoice = new Invoice
                                {
                                    InvoiceStartDate = DateTime.Now,
                                    InvoiceEndDate = DateTime.Now.AddMonths(1),
                                    AddedDate = DateTime.Now,
                                    AddedBy = getuserinfo.FullName,
                                    SubscriptionAmount = Convert.ToInt32(decimal.Round(after_vat_totalamount)),
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
                                };
                                _context.invoices.Add(invoice);
                                _context.SaveChanges();
                                int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
                                var nameinvoice = "Inv" + max_invoice_id;
                                _ = _emailSender.SendEmailAsync(getuserinfo.Email, "Tamarran - Your subscription renewal in Tamarran failed", "Hello " + getuserinfo.GYMName + ",<br /><br />On " + DateTime.Now.ToShortDateString() + ", the payment for " + nameinvoice + " failed.<br /><br />Update your card details via: " + RedirectURL + " <br /><br /><br />Please update your credit or debit card details through the above link as soon as possible to complete the payment.<br /><br />If you have any questions, please e-mail us at accounts@tamarran.com or call us on +973-36021122 or +966-557070136.<br /><br />Thanks,<br /><br />Tamarran Team");
                            }
                        }
                        else if (getuserinfo.Frequency == "QUARTERLY")
                        {
                            // Create a charge
                            var client = new HttpClient();
                            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tap.company/v2/charges");
                            request.Headers.Add("Authorization", "Bearer " + getuserinfo.SecertKey);
                            request.Headers.Add("accept", "application/json");
                            var content = new StringContent("\r\n{\r\n  \"amount\": " + Decimal.Round(after_vat_totalamount) + ",\r\n  \"currency\": \"" + getsubinfo.Currency + "\",\r\n  \"customer_initiated\": false,\r\n  \"threeDSecure\": true,\r\n  \"save_card\": false,\r\n  \"payment_agreement\": {\r\n    \"contract\": {\r\n      \"id\": \"" + getuserinfo.Tap_Card_ID + "\"\r\n    },\r\n    \"id\": \"" + getuserinfo.Tap_Agreement_ID + "\"\r\n  },\r\n  \"receipt\": {\r\n    \"email\": true,\r\n    \"sms\": true\r\n  },\"reference\": {\r\n    \"transaction\": \"" + TransNo + "\",\r\n    \"order\": \"" + OrderNo + "\"\r\n  },\r\n  \"customer\": {\r\n    \"id\": \"" + getuserinfo.Tap_CustomerID + "\"\r\n  },\r\n  \"source\": {\r\n    \"id\": \"" + Deserialized_savecard.id + "\"\r\n  },\r\n  \"redirect\": {\r\n    \"url\": \"https://1f3b186efe31e8696c144578816c5443.m.pipedream.net/\"\r\n  }\r\n}\r\n", null, "application/json");
                            request.Content = content;
                            var response = await client.SendAsync(request);
                            var bodys = await response.Content.ReadAsStringAsync();
                            deserialized_CreateCharge = JsonConvert.DeserializeObject<CreateCharge>(bodys);
                            if (deserialized_CreateCharge.status == "CAPTURED")
                            {
                                Invoice invoice = new Invoice
                                {
                                    InvoiceStartDate = DateTime.Now,
                                    InvoiceEndDate = DateTime.Now.AddMonths(3),
                                    AddedDate = DateTime.Now,
                                    AddedBy = getuserinfo.FullName,
                                    Currency = getsubinfo.Currency,
                                    SubscriptionAmount = Convert.ToInt32(decimal.Round(after_vat_totalamount)),
                                    SubscriptionId = getsubinfo.SubscriptionId,
                                    Status = "Payment Captured",
                                    VAT = Vat.ToString(),
                                    Discount = Discount.ToString(),
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

                                // Update Job Table
                                var recurreningjob = _context.recurringCharges.Where(x => x.RecurringChargeId == item.RecurringChargeId).FirstOrDefault();
                                recurreningjob.IsRun = true;
                                recurreningjob.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                _context.recurringCharges.Update(recurreningjob);
                                _context.SaveChanges();
                                //Send Email
                                int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
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
                                body = body.Replace("{currentdate}", DateTime.Now.ToString());

                                body = body.Replace("{InvocieStatus}", "Payment Captured");
                                body = body.Replace("{InvoiceID}", "Inv" + max_invoice_id);


                                body = body.Replace("{User_Name}", getuserinfo.FullName);
                                body = body.Replace("{User_Email}", getuserinfo.Email);
                                body = body.Replace("{User_GYM}", getuserinfo.GYMName);
                                body = body.Replace("{User_Phone}", getuserinfo.PhoneNumber);

                                body = body.Replace("{SubscriptionName}", getsubinfo.Name);
                                body = body.Replace("{Discount}", Discount.ToString());
                                body = body.Replace("{SubscriptionPeriod}", getuserinfo.Frequency);
                                body = body.Replace("{SetupFee}", getsubinfo.SetupFee + " " + getsubinfo.Currency);
                                int amount = Convert.ToInt32(incoice_info.SubscriptionAmount);
                                body = body.Replace("{SubscriptionAmount}", decimal.Round(sun_amount, 2).ToString() + " " + getsubinfo.Currency);
                                //Calculate VAT
                                if (getsubinfo.VAT == null)
                                {
                                    body = body.Replace("{VAT}", "0.00");
                                    body = body.Replace("{Total}", amount.ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{InvoiceAmount}", amount.ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(amount), 2).ToString() + " " + getsubinfo.Currency);
                                }
                                else
                                {
                                    body = body.Replace("{VAT}", decimal.Round(Convert.ToDecimal(Vat), 2).ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{Total}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{InvoiceAmount}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(amount), 2).ToString() + " " + getsubinfo.Currency);
                                }
                                var bytes = (new NReco.PdfGenerator.HtmlToPdfConverter()).GeneratePdf(body);
                                _ = _emailSender.SendEmailWithFIle(bytes, getuserinfo.Email, "Tamarran - Automatic Payment Confirmation", "Hello " + getuserinfo.GYMName + ",<br />Kindly note that your subscription payment to Tamarran was made successfully.<br />Attached is the receipt for subscription auto-payment transaction.<br />Thank you for your business.<br />Tamarran's team!");
                            }
                            else
                            {
                                Invoice invoice = new Invoice
                                {
                                    InvoiceStartDate = DateTime.Now,
                                    InvoiceEndDate = DateTime.Now.AddMonths(3),
                                    AddedDate = DateTime.Now,
                                    AddedBy = getuserinfo.FullName,
                                    Currency = getsubinfo.Currency,
                                    SubscriptionAmount = Convert.ToInt32(decimal.Round(after_vat_totalamount)),
                                    SubscriptionId = getsubinfo.SubscriptionId,
                                    Status = "Un-Paid",
                                    VAT = Vat.ToString(),
                                    Discount = Discount.ToString(),
                                    IsDeleted = false,
                                    Description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")",
                                    SubscriptionName = getsubinfo.Name,
                                    UserId = getuserinfo.Id,
                                    ChargeId = deserialized_CreateCharge.id,
                                };
                                _context.invoices.Add(invoice);
                                _context.SaveChanges();
                                int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
                                var nameinvoice = "Inv" + max_invoice_id;
                                _ = _emailSender.SendEmailAsync(getuserinfo.Email, "Tamarran - Your subscription renewal in Tamarran failed", "Hello " + getuserinfo.GYMName + ",<br /><br />On " + DateTime.Now.ToShortDateString() + ", the payment for " + nameinvoice + " failed.<br /><br />Update your card details via: " + RedirectURL + " <br /><br /><br />Please update your credit or debit card details through the above link as soon as possible to complete the payment.<br /><br />If you have any questions, please e-mail us at accounts@tamarran.com or call us on +973-36021122 or +966-557070136.<br /><br />Thanks,<br /><br />Tamarran Team");
                            }
                        }
                        else if (getuserinfo.Frequency == "HALFYEARLY")
                        {
                            // Create a charge
                            var client = new HttpClient();
                            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tap.company/v2/charges");
                            request.Headers.Add("Authorization", "Bearer " + getuserinfo.SecertKey);
                            request.Headers.Add("accept", "application/json");
                            var content = new StringContent("\r\n{\r\n  \"amount\": " + Decimal.Round(after_vat_totalamount) + ",\r\n  \"currency\": \"" + getsubinfo.Currency + "\",\r\n  \"customer_initiated\": false,\r\n  \"threeDSecure\": true,\r\n  \"save_card\": false,\r\n  \"payment_agreement\": {\r\n    \"contract\": {\r\n      \"id\": \"" + getuserinfo.Tap_Card_ID + "\"\r\n    },\r\n    \"id\": \"" + getuserinfo.Tap_Agreement_ID + "\"\r\n  },\r\n  \"receipt\": {\r\n    \"email\": true,\r\n    \"sms\": true\r\n  },\"reference\": {\r\n    \"transaction\": \"" + TransNo + "\",\r\n    \"order\": \"" + OrderNo + "\"\r\n  },\r\n  \"customer\": {\r\n    \"id\": \"" + getuserinfo.Tap_CustomerID + "\"\r\n  },\r\n  \"source\": {\r\n    \"id\": \"" + Deserialized_savecard.id + "\"\r\n  },\r\n  \"redirect\": {\r\n    \"url\": \"https://1f3b186efe31e8696c144578816c5443.m.pipedream.net/\"\r\n  }\r\n}\r\n", null, "application/json");
                            request.Content = content;
                            var response = await client.SendAsync(request);
                            var bodys = await response.Content.ReadAsStringAsync();
                            deserialized_CreateCharge = JsonConvert.DeserializeObject<CreateCharge>(bodys);
                            if (deserialized_CreateCharge.status == "CAPTURED")
                            {
                                Invoice invoice = new Invoice
                                {
                                    InvoiceStartDate = DateTime.Now,
                                    InvoiceEndDate = DateTime.Now.AddMonths(6),
                                    AddedDate = DateTime.Now,
                                    VAT = Vat.ToString(),
                                    Discount = Discount.ToString(),
                                    AddedBy = getuserinfo.FullName,
                                    SubscriptionAmount = Convert.ToInt32(decimal.Round(after_vat_totalamount)),
                                    Currency = getsubinfo.Currency,
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

                                // Update Job Table
                                var recurreningjob = _context.recurringCharges.Where(x => x.RecurringChargeId == item.RecurringChargeId).FirstOrDefault();
                                recurreningjob.IsRun = true;
                                recurreningjob.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                _context.recurringCharges.Update(recurreningjob);
                                _context.SaveChanges();
                                //Send Email
                                int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
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
                                body = body.Replace("{currentdate}", DateTime.Now.ToString());

                                body = body.Replace("{InvocieStatus}", "Payment Captured");
                                body = body.Replace("{InvoiceID}", "Inv" + max_invoice_id);


                                body = body.Replace("{User_Name}", getuserinfo.FullName);
                                body = body.Replace("{User_Email}", getuserinfo.Email);
                                body = body.Replace("{User_GYM}", getuserinfo.GYMName);
                                body = body.Replace("{User_Phone}", getuserinfo.PhoneNumber);

                                body = body.Replace("{SubscriptionName}", getsubinfo.Name);
                                body = body.Replace("{Discount}", Discount.ToString());
                                body = body.Replace("{SubscriptionPeriod}", getuserinfo.Frequency);
                                body = body.Replace("{SetupFee}", getsubinfo.SetupFee + " " + getsubinfo.Currency);
                                int amount = Convert.ToInt32(incoice_info.SubscriptionAmount);
                                body = body.Replace("{SubscriptionAmount}", decimal.Round(sun_amount, 2).ToString() + " " + getsubinfo.Currency);
                                //Calculate VAT
                                if (getsubinfo.VAT == null)
                                {
                                    body = body.Replace("{VAT}", "0.00");
                                    body = body.Replace("{Total}", amount.ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{InvoiceAmount}", amount.ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(amount), 2).ToString() + " " + getsubinfo.Currency);
                                }
                                else
                                {
                                    body = body.Replace("{VAT}", decimal.Round(Convert.ToDecimal(Vat), 2).ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{Total}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{InvoiceAmount}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(amount), 2).ToString() + " " + getsubinfo.Currency);
                                }
                                var bytes = (new NReco.PdfGenerator.HtmlToPdfConverter()).GeneratePdf(body);
                                _ = _emailSender.SendEmailWithFIle(bytes, getuserinfo.Email, "Tamarran - Automatic Payment Confirmation", "Hello " + getuserinfo.GYMName + ",<br />Kindly note that your subscription payment to Tamarran was made successfully.<br />Attached is the receipt for subscription auto-payment transaction.<br />Thank you for your business.<br />Tamarran's team!");
                            }
                            else
                            {
                                Invoice invoice = new Invoice
                                {
                                    InvoiceStartDate = DateTime.Now,
                                    InvoiceEndDate = DateTime.Now.AddMonths(6),
                                    AddedDate = DateTime.Now,
                                    VAT = Vat.ToString(),
                                    Discount = Discount.ToString(),
                                    AddedBy = getuserinfo.FullName,
                                    SubscriptionAmount = Convert.ToInt32(decimal.Round(after_vat_totalamount)),
                                    Currency = getsubinfo.Currency,
                                    SubscriptionId = getsubinfo.SubscriptionId,
                                    Status = "Un-Paid",
                                    IsDeleted = false,
                                    Description = "Invoice Create - Frequency(" + getuserinfo.Frequency + ")",
                                    SubscriptionName = getsubinfo.Name,
                                    UserId = getuserinfo.Id,
                                    ChargeId = deserialized_CreateCharge.id,
                                };
                                _context.invoices.Add(invoice);
                                _context.SaveChanges();
                                int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
                                var nameinvoice = "Inv" + max_invoice_id;
                                _ = _emailSender.SendEmailAsync(getuserinfo.Email, "Tamarran - Your subscription renewal in Tamarran failed", "Hello " + getuserinfo.GYMName + ",<br /><br />On " + DateTime.Now.ToShortDateString() + ", the payment for " + nameinvoice + " failed.<br /><br />Update your card details via: " + RedirectURL + " <br /><br /><br />Please update your credit or debit card details through the above link as soon as possible to complete the payment.<br /><br />If you have any questions, please e-mail us at accounts@tamarran.com or call us on +973-36021122 or +966-557070136.<br /><br />Thanks,<br /><br />Tamarran Team");
                            }
                        }
                        else if (getuserinfo.Frequency == "YEARLY")
                        {
                            // Create a charge
                            var client = new HttpClient();
                            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tap.company/v2/charges");
                            request.Headers.Add("Authorization", "Bearer " + getuserinfo.SecertKey);
                            request.Headers.Add("accept", "application/json");
                            var content = new StringContent("\r\n{\r\n  \"amount\": " + Decimal.Round(after_vat_totalamount) + ",\r\n  \"currency\": \"" + getsubinfo.Currency + "\",\r\n  \"customer_initiated\": false,\r\n  \"threeDSecure\": true,\r\n  \"save_card\": false,\r\n  \"payment_agreement\": {\r\n    \"contract\": {\r\n      \"id\": \"" + getuserinfo.Tap_Card_ID + "\"\r\n    },\r\n    \"id\": \"" + getuserinfo.Tap_Agreement_ID + "\"\r\n  },\r\n  \"receipt\": {\r\n    \"email\": true,\r\n    \"sms\": true\r\n  },\"reference\": {\r\n    \"transaction\": \"" + TransNo + "\",\r\n    \"order\": \"" + OrderNo + "\"\r\n  },\r\n  \"customer\": {\r\n    \"id\": \"" + getuserinfo.Tap_CustomerID + "\"\r\n  },\r\n  \"source\": {\r\n    \"id\": \"" + Deserialized_savecard.id + "\"\r\n  },\r\n  \"redirect\": {\r\n    \"url\": \"https://1f3b186efe31e8696c144578816c5443.m.pipedream.net/\"\r\n  }\r\n}\r\n", null, "application/json");
                            request.Content = content;
                            var response = await client.SendAsync(request);
                            var bodys = await response.Content.ReadAsStringAsync();
                            deserialized_CreateCharge = JsonConvert.DeserializeObject<CreateCharge>(bodys);
                            if (deserialized_CreateCharge.status == "CAPTURED")
                            {
                                Invoice invoice = new Invoice
                                {
                                    InvoiceStartDate = DateTime.Now,
                                    InvoiceEndDate = DateTime.Now.AddMonths(12),
                                    AddedDate = DateTime.Now,
                                    AddedBy = getuserinfo.FullName,
                                    SubscriptionAmount = Convert.ToInt32(decimal.Round(after_vat_totalamount)),
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

                                // Update Job Table
                                var recurreningjob = _context.recurringCharges.Where(x => x.RecurringChargeId == item.RecurringChargeId).FirstOrDefault();
                                recurreningjob.IsRun = true;
                                recurreningjob.Tap_CustomerId = getuserinfo.Tap_CustomerID;
                                _context.recurringCharges.Update(recurreningjob);
                                _context.SaveChanges();
                                //Send Email
                                int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
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
                                body = body.Replace("{currentdate}", DateTime.Now.ToString());

                                body = body.Replace("{InvocieStatus}", "Payment Captured");
                                body = body.Replace("{InvoiceID}", "Inv" + max_invoice_id);


                                body = body.Replace("{User_Name}", getuserinfo.FullName);
                                body = body.Replace("{User_Email}", getuserinfo.Email);
                                body = body.Replace("{User_GYM}", getuserinfo.GYMName);
                                body = body.Replace("{User_Phone}", getuserinfo.PhoneNumber);

                                body = body.Replace("{SubscriptionName}", getsubinfo.Name);
                                body = body.Replace("{Discount}", Discount.ToString());
                                body = body.Replace("{SubscriptionPeriod}", getuserinfo.Frequency);
                                body = body.Replace("{SetupFee}", getsubinfo.SetupFee + " " + getsubinfo.Currency);
                                int amount = Convert.ToInt32(incoice_info.SubscriptionAmount);
                                body = body.Replace("{SubscriptionAmount}", decimal.Round(sun_amount, 2).ToString() + " " + getsubinfo.Currency);
                                //Calculate VAT
                                if (getsubinfo.VAT == null)
                                {
                                    body = body.Replace("{VAT}", "0.00");
                                    body = body.Replace("{Total}", amount.ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{InvoiceAmount}", amount.ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(amount), 2).ToString() + " " + getsubinfo.Currency);
                                }
                                else
                                {
                                    body = body.Replace("{VAT}", decimal.Round(Convert.ToDecimal(Vat), 2).ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{Total}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{InvoiceAmount}", decimal.Round(Convert.ToDecimal(after_vat_totalamount), 2).ToString() + " " + getsubinfo.Currency);
                                    body = body.Replace("{Totalinvoicewithoutvat}", decimal.Round(Convert.ToDecimal(amount), 2).ToString() + " " + getsubinfo.Currency);
                                }
                                var bytes = (new NReco.PdfGenerator.HtmlToPdfConverter()).GeneratePdf(body);
                                _ = _emailSender.SendEmailWithFIle(bytes, getuserinfo.Email, "Tamarran - Automatic Payment Confirmation", "Hello " + getuserinfo.GYMName + ",<br />Kindly note that your subscription payment to Tamarran was made successfully.<br />Attached is the receipt for subscription auto-payment transaction.<br />Thank you for your business.<br />Tamarran's team!");
                            }
                            else
                            {
                                Invoice invoice = new Invoice
                                {
                                    InvoiceStartDate = DateTime.Now,
                                    InvoiceEndDate = DateTime.Now.AddMonths(12),
                                    AddedDate = DateTime.Now,
                                    AddedBy = getuserinfo.FullName,
                                    SubscriptionAmount = Convert.ToInt32(decimal.Round(after_vat_totalamount)),
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
                                };
                                _context.invoices.Add(invoice);
                                _context.SaveChanges();
                                int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
                                var nameinvoice = "Inv" + max_invoice_id;
                                _ = _emailSender.SendEmailAsync(getuserinfo.Email, "Tamarran - Your subscription renewal in Tamarran failed", "Hello " + getuserinfo.GYMName + ",<br /><br />On " + DateTime.Now.ToShortDateString() + ", the payment for " + nameinvoice + " failed.<br /><br />Update your card details via: " + RedirectURL + " <br /><br /><br />Please update your credit or debit card details through the above link as soon as possible to complete the payment.<br /><br />If you have any questions, please e-mail us at accounts@tamarran.com or call us on +973-36021122 or +966-557070136.<br /><br />Thanks,<br /><br />Tamarran Team");
                            }
                        }
                    }
                }
            }

        }
    }
}
