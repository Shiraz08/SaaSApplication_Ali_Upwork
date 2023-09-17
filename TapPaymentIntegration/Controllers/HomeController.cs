using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json;
using TapPaymentIntegration.Areas.Identity.Data;
using TapPaymentIntegration.Data;
using TapPaymentIntegration.Models.Email;
using TapPaymentIntegration.Models.InvoiceDTO;
using TapPaymentIntegration.Models.PaymentDTO;
using TapPaymentIntegration.Models.UserDTO;
using TapPaymentIntegration.Models.Card;
using ApplicationUser = TapPaymentIntegration.Areas.Identity.Data.ApplicationUser;
using System.Text.Encodings.Web;

namespace TapPaymentIntegration.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private TapPaymentIntegrationContext _context;
        private readonly IUserStore<ApplicationUser> _userStore;
        private IWebHostEnvironment _environment; 
        private Task<ApplicationUser> GetCurrentUserAsync() => _userManager.GetUserAsync(HttpContext.User);
        EmailSender _emailSender = new EmailSender();
        // Change Your Keys & URL's Here
        public readonly string BHD_Public_Key = "pk_test_7sAiZNXvdpKax26RuJMwbIen"; 
        public readonly string BHD_Test_Key = "sk_test_Tgoy8HbxdQ40l6Ea9SIDci7B";
        public readonly string KSA_Public_Key = "pk_test_j3yKfvbxws8khDpFQOX5JeWc"; 
        public readonly string KSA_Test_Key = "sk_test_1SU5woL8vZe6JXrBHipQu9Dn";
        public readonly string RedirectURL = "https://localhost:7279";
        public HomeController(IWebHostEnvironment Environment, ILogger<HomeController> logger, SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager, TapPaymentIntegrationContext context, IUserStore<ApplicationUser> userStore)
        {
            _logger = logger;
            _signInManager = signInManager; 
            _userManager = userManager; 
            _context = context;
            _userStore = userStore;
            _environment = Environment; 
        }

        #region Website 
        public IActionResult Index(string id , string userid)
        {
            var subscriptions = _context.subscriptions.Where(x => x.Status == true).ToList();
            return View(subscriptions); 
        }
        public async Task<IActionResult> Subscription(int id,string link,string userid)
        {
            if(link != null)
            {
                var applicationUser = _context.Users.Where(x => x.Id == userid).FirstOrDefault();
                await _signInManager.PasswordSignInAsync(applicationUser.UserName.ToString(), applicationUser.Password, true, lockoutOnFailure: true);
            }
            var subscriptions = _context.subscriptions.Where(x => x.Status == true && x.SubscriptionId == id).FirstOrDefault();
            var users = _context.Users.Where(x => x.Status == true && x.SubscribeID == id).FirstOrDefault();
            return View(subscriptions);
        }
        public async Task<IActionResult> Logout()
        {
            var returnUrl = Url.Action("Index", "Home");
            await _signInManager.SignOutAsync();
            return LocalRedirect(returnUrl);
        }
        #endregion

        #region Tap Charge API
        [HttpPost]
        public async Task<IActionResult> CreateInvoice()
        {
            var Frequency = Request.Form.Where(x => x.Key == "Frequency").FirstOrDefault().Value.ToString();
            var TotalPlanfee = Request.Form.Where(x => x.Key == "TotalPlanfee").FirstOrDefault().Value.ToString();
            var SubscriptionId = Request.Form.Where(x => x.Key == "SubscriptionId").FirstOrDefault().Value.ToString();
            var Token = Request.Form.Where(x => x.Key == "Token").FirstOrDefault().Value.ToString();
            if (SubscriptionId != null && Frequency != null)
            {
                var userinfo = _context.Users.Where(x => x.Id == GetCurrentUserAsync().Result.Id).FirstOrDefault();
                var subscriptions = _context.subscriptions.Where(x => x.Status == true && x.SubscriptionId == Convert.ToInt32(SubscriptionId)).FirstOrDefault();
                Random rnd = new Random();
                var TransNo = "Txn_" + rnd.Next(10000000, 99999999);
                var OrderNo = "Ord_" + rnd.Next(10000000, 99999999);
                var currency = userinfo.Currency;
                var amount = decimal.Round(Convert.ToDecimal(TotalPlanfee));
                var description = subscriptions.Frequency;
                Reference reference = new Reference();
                reference.transaction = TransNo;
                reference.order = OrderNo;

                Redirect redirect = new Redirect();
                redirect.url = RedirectURL + "/Home/CardVerify";

                Post post = new Post();
                post.url = RedirectURL + "/Home/CardVerify";


                var countrycode = "";
                var currencycode = "";
                if (userinfo.Country == "Bahrain")
                {
                    countrycode = "+973";
                    currencycode = "BHD";
                }
                else if (userinfo.Country == "KSA")
                {
                    countrycode = "+966";
                    currencycode = "SAR";
                }
                else if (userinfo.Country == "Kuwait")
                {
                    countrycode = "+965";
                    currencycode = "KWD";
                }
                else if (userinfo.Country == "UAE")
                {
                    countrycode = "+971";
                    currencycode = "AED";
                }
                else if (userinfo.Country == "Qatar")
                {
                    countrycode = "+974";
                    currencycode = "QAR";
                }
                else if (userinfo.Country == "Oman")
                {
                    countrycode = "+968";
                    currencycode = "OMR";
                }
                Phone phone = new Phone();
                phone.number = userinfo.PhoneNumber;
                phone.country_code = countrycode;



                Customer customer = new Customer();
                customer.first_name = GetCurrentUserAsync().Result.FullName;
                customer.email = GetCurrentUserAsync().Result.Email;
                customer.phone = phone;

                Receipt receipt = new Receipt();
                receipt.sms = true;
                receipt.email = true;

                Metadata metadata = new Metadata();
                metadata.udf1 = "Metadata 1";

                Source source = new Source();
                source.id = Token;

                Merchant merchant = new Merchant();
                merchant.id = "22116401";

                FillChargeModel fillChargeModel = new FillChargeModel();
                fillChargeModel.threeDSecure = true;
                fillChargeModel.amount = Convert.ToInt32(amount);
                fillChargeModel.save_card = true;
                fillChargeModel.currency = currency;
                fillChargeModel.redirect = redirect;
                fillChargeModel.post = post;
                fillChargeModel.customer = customer;
                fillChargeModel.metadata = metadata;
                fillChargeModel.reference = reference;
                fillChargeModel.receipt = receipt;
                fillChargeModel.source = source;
                fillChargeModel.merchant = merchant;
                fillChargeModel.customer_initiated = true;
                var jsonmodel = JsonConvert.SerializeObject(fillChargeModel);
                var client_charge = new HttpClient();
                var request_charge = new HttpRequestMessage(HttpMethod.Post, "https://api.tap.company/v2/charges");
                request_charge.Headers.Add("Authorization", "Bearer " + GetCurrentUserAsync().Result.SecertKey);
                request_charge.Headers.Add("accept", "application/json");
                var content_charge = new StringContent(jsonmodel, null, "application/json");
                request_charge.Content = content_charge;
                var response_charge = await client_charge.SendAsync(request_charge);
                var body = await response_charge.Content.ReadAsStringAsync();
                CreateCharge deserialized_CreateCharge = JsonConvert.DeserializeObject<CreateCharge>(body);

                HttpContext.Session.SetString("SubscriptionId", SubscriptionId);
                HttpContext.Session.SetString("Frequency", Frequency);
                HttpContext.Session.SetString("Token", Token);
                ChargeResponse chargeResponse = new ChargeResponse
                {
                    UserId = userinfo.Id,
                    ChargeId = deserialized_CreateCharge.id,
                    amount = deserialized_CreateCharge.amount,
                    currency = currency,
                    status = deserialized_CreateCharge.status,
                };
                _context.chargeResponses.Add(chargeResponse);
                _context.SaveChanges();
                //update user 
                userinfo.Tap_CustomerID = deserialized_CreateCharge.customer.id;
                userinfo.Frequency = Frequency;
                _context.Users.Update(userinfo);
                _context.SaveChanges();
                return Json(deserialized_CreateCharge.transaction.url);
            }
            return Json(false);
        }
        public async Task<IActionResult> CardVerify()
        {
            string tap_id = HttpContext.Request.Query["tap_id"].ToString();
            ChargeDetail Deserialized_savecard = null;
            if (tap_id != null)
            {

                //Get Charge Detail
                var client_ChargeDetail = new HttpClient();
                var request_ChargeDetail = new HttpRequestMessage(HttpMethod.Get, "https://api.tap.company/v2/charges/" + tap_id);
                request_ChargeDetail.Headers.Add("Authorization", "Bearer " + GetCurrentUserAsync().Result.SecertKey);
                request_ChargeDetail.Headers.Add("accept", "application/json");
                var response_ChargeDetail = await client_ChargeDetail.SendAsync(request_ChargeDetail);
                var result_ChargeDetail = await response_ChargeDetail.Content.ReadAsStringAsync();
                Deserialized_savecard = JsonConvert.DeserializeObject<ChargeDetail>(result_ChargeDetail);
            }
            var SubscriptionId = HttpContext.Session.GetString("SubscriptionId");
            var Frequency = HttpContext.Session.GetString("Frequency");
            if (Deserialized_savecard.id != null)
            {
                //Create Invoice
                var users = GetCurrentUserAsync().Result;
                var subscriptions = _context.subscriptions.Where(x => x.Status == true && x.SubscriptionId == Convert.ToInt32(SubscriptionId)).FirstOrDefault();
                var Amount = subscriptions.Amount;
                int days = DateTime.DaysInMonth(DateTime.Now.Year, DateTime.Now.Month);
                int finalamount = 0;
                if (Frequency == "DAILY")
                {
                    finalamount = Convert.ToInt32(subscriptions.Amount) / days;
                    Invoice invoices = new Invoice
                    {
                        InvoiceStartDate = DateTime.Now,
                        InvoiceEndDate = DateTime.Now,
                        AddedDate = DateTime.Now,
                        AddedBy = users.FullName,
                        SubscriptionAmount = finalamount + Convert.ToInt32(subscriptions.SetupFee),
                        SubscriptionId = Convert.ToInt32(SubscriptionId),
                        Status = "Un-Paid",
                        IsDeleted = false,
                        Description = "Invoice Create - Frequency(" + Frequency + ")",
                        SubscriptionName = subscriptions.Name,
                        UserId = users.Id,
                        ChargeId = tap_id,
                    };
                    _context.invoices.Add(invoices);
                    _context.SaveChanges();
                }
                else if (Frequency == "WEEKLY")
                {
                    finalamount = Convert.ToInt32(subscriptions.Amount) / 4;
                    Invoice invoices = new Invoice
                    {
                        InvoiceStartDate = DateTime.Now,
                        InvoiceEndDate = DateTime.Now.AddDays(7),
                        AddedDate = DateTime.Now,
                        AddedBy = users.FullName,
                        SubscriptionAmount = finalamount + Convert.ToInt32(subscriptions.SetupFee),
                        SubscriptionId = Convert.ToInt32(SubscriptionId),
                        Status = "Un-Paid",
                        IsDeleted = false,
                        Description = "Invoice Create - Frequency(" + Frequency + ")",
                        SubscriptionName = subscriptions.Name,
                        UserId = users.Id,
                        ChargeId = tap_id,
                    };
                    _context.invoices.Add(invoices);
                    _context.SaveChanges();
                }
                else if (Frequency == "MONTHLY")
                {
                    finalamount = Convert.ToInt32(subscriptions.Amount) / 1;
                    Invoice invoices = new Invoice
                    {
                        InvoiceStartDate = DateTime.Now,
                        InvoiceEndDate = DateTime.Now.AddMonths(1),
                        AddedDate = DateTime.Now,
                        AddedBy = users.FullName,
                        SubscriptionAmount = finalamount + Convert.ToInt32(subscriptions.SetupFee),
                        SubscriptionId = Convert.ToInt32(SubscriptionId),
                        Status = "Un-Paid",
                        IsDeleted = false,
                        Description = "Invoice Create - Frequency(" + Frequency + ")",
                        SubscriptionName = subscriptions.Name,
                        UserId = users.Id,
                        ChargeId = tap_id,
                    };
                    _context.invoices.Add(invoices);
                    _context.SaveChanges();
                }
                else if (Frequency == "QUARTERLY")
                {
                    finalamount = (Convert.ToInt32(subscriptions.Amount) * 3) / 1;
                    Invoice invoices = new Invoice
                    {
                        InvoiceStartDate = DateTime.Now,
                        InvoiceEndDate = DateTime.Now.AddMonths(3),
                        AddedDate = DateTime.Now,
                        AddedBy = users.FullName,
                        SubscriptionAmount = finalamount + Convert.ToInt32(subscriptions.SetupFee),
                        SubscriptionId = Convert.ToInt32(SubscriptionId),
                        Status = "Un-Paid",
                        IsDeleted = false,
                        Description = "Invoice Create - Frequency(" + Frequency + ")",
                        SubscriptionName = subscriptions.Name,
                        UserId = users.Id,
                        ChargeId = tap_id,
                    };
                    _context.invoices.Add(invoices);
                    _context.SaveChanges();
                }
                else if (Frequency == "HALFYEARLY")
                {
                    finalamount = (Convert.ToInt32(subscriptions.Amount) * 6) / 1;
                    Invoice invoices = new Invoice
                    {
                        InvoiceStartDate = DateTime.Now,
                        InvoiceEndDate = DateTime.Now.AddMonths(6),
                        AddedDate = DateTime.Now,
                        AddedBy = users.FullName,
                        SubscriptionAmount = finalamount + Convert.ToInt32(subscriptions.SetupFee),
                        SubscriptionId = Convert.ToInt32(SubscriptionId),
                        Status = "Un-Paid",
                        IsDeleted = false,
                        Description = "Invoice Create - Frequency(" + Frequency + ")",
                        SubscriptionName = subscriptions.Name,
                        UserId = users.Id,
                        ChargeId = tap_id,
                    };
                    _context.invoices.Add(invoices);
                    _context.SaveChanges();
                }
                else if (Frequency == "YEARLY")
                {
                    var discount_amount = (Convert.ToInt32(subscriptions.Amount) / 100) * 10;
                    finalamount = (Convert.ToInt32(subscriptions.Amount) - discount_amount) * 12;
                    Invoice invoices = new Invoice
                    {
                        InvoiceStartDate = DateTime.Now,
                        InvoiceEndDate = DateTime.Now.AddMonths(12),
                        AddedDate = DateTime.Now,
                        AddedBy = users.FullName,
                        SubscriptionAmount = finalamount + Convert.ToInt32(subscriptions.SetupFee),
                        SubscriptionId = Convert.ToInt32(SubscriptionId),
                        Status = "Un-Paid",
                        IsDeleted = false,
                        Description = "Invoice Create - Frequency(" + Frequency + ")",
                        SubscriptionName = subscriptions.Name,
                        UserId = users.Id,
                        ChargeId = tap_id,
                    };
                    _context.invoices.Add(invoices);
                    _context.SaveChanges();
                }
                // Update Recurring Job data
                int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
                DateTime nextrecurringdate = _context.invoices.Where(x => x.InvoiceId == max_invoice_id).Select(x => x.InvoiceEndDate).FirstOrDefault();
                RecurringCharge recurringCharge = new RecurringCharge();
                recurringCharge.Amount = Convert.ToDecimal(finalamount);
                recurringCharge.SubscriptionId = subscriptions.SubscriptionId;
                recurringCharge.UserID = users.Id;
                recurringCharge.Tap_CustomerId = users.Tap_CustomerID;
                recurringCharge.ChargeId = tap_id;
                recurringCharge.IsRun = false;
                recurringCharge.JobRunDate = nextrecurringdate.AddDays(1);
                _context.recurringCharges.Add(recurringCharge);
                _context.SaveChanges();


                var userinfo = _context.Users.Where(x => x.Id == users.Id).FirstOrDefault();
                var invoice = _context.invoices.Where(x => x.InvoiceId == max_invoice_id).FirstOrDefault();


                //update user 
                users.Tap_CustomerID = Deserialized_savecard.payment_agreement.contract.customer_id;
                users.Tap_Card_ID = Deserialized_savecard.payment_agreement.contract.id;
                users.SubscribeID = Convert.ToInt32(SubscriptionId);
                users.Tap_Agreement_ID = Deserialized_savecard.payment_agreement.id;
                users.PaymentSource = Deserialized_savecard.source.payment_method;
                _context.Users.Update(users);
                _context.SaveChanges();

                int getchargesresposemodel = _context.chargeResponses.Max(x => x.ChargeResponseId);
                Invoice invoice_info = _context.invoices.Find(max_invoice_id);
                invoice_info.ChargeId = tap_id;
                invoice_info.Status = "Payment Captured";
                invoice_info.ChargeResponseId = getchargesresposemodel;
                _context.invoices.Update(invoice_info);
                _context.SaveChanges();
                // Send Email
                string body = string.Empty;
                _environment.WebRootPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                string contentRootPath = _environment.WebRootPath + "/htmltopdf.html";
                string contentRootPath1 = _environment.WebRootPath + "/css/bootstrap.min.css";
                //Generate PDF
                var cr = _context.chargeResponses.Where(x => x.ChargeId == tap_id).FirstOrDefault();
                var sub_id = HttpContext.Session.GetString("SubscriptionId");
                var sub_info = _context.subscriptions.Where(x => x.SubscriptionId == Convert.ToInt32(sub_id)).FirstOrDefault();
                using (StreamReader reader = new StreamReader(contentRootPath))
                {
                    body = reader.ReadToEnd();
                }
                //Fill EMail By Parameter
                body = body.Replace("{title}", "Tamarran Payment Invoice");
                body = body.Replace("{currentdate}", DateTime.Now.ToString());

                body = body.Replace("{InvocieStatus}", "Payment Captured");
                body = body.Replace("{InvoiceID}", "Inv" + "233");
                body = body.Replace("{InvoiceAmount}", "23");


                body = body.Replace("{User_Name}", userinfo.FullName);
                body = body.Replace("{User_Email}", userinfo.Email);
                body = body.Replace("{User_Country}", userinfo.Country);
                body = body.Replace("{User_Phone}", userinfo.PhoneNumber);


                body = body.Replace("{SubscriptionName}", sub_info.Name);
                body = body.Replace("{SubscriptionPeriod}", sub_info.Frequency);
                body = body.Replace("{VAT}", "0.00");
                body = body.Replace("{SetupFee}", sub_info.SetupFee);
                int amount = Convert.ToInt32(finalamount) + Convert.ToInt32(sub_info.SetupFee);
                body = body.Replace("{Total}", amount.ToString());
                body = body.Replace("{SubscriptionAmount}", finalamount.ToString());

                var renderer = new ChromePdfRenderer();
                // Many rendering options to use to customize!
                renderer.RenderingOptions.SetCustomPaperSizeInInches(6.9, 12);
                renderer.RenderingOptions.PaperOrientation = IronPdf.Rendering.PdfPaperOrientation.Portrait;
                renderer.RenderingOptions.Title = "My PDF Document Name";
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

                _ = _emailSender.SendEmailWithFIle(bytes, userinfo.Email, "Payment Captured", "Your Payment has been received successfully. Thank you.");
                return RedirectToAction("ShowInvoice", "Home", new { PaymentStatus = "All" });
            }
            else
            {
                //Update Charge Response;
                int getchargesresposemodel = _context.chargeResponses.Max(x => x.ChargeResponseId);
                var chargeresponse = _context.chargeResponses.Where(x => x.ChargeResponseId == getchargesresposemodel).FirstOrDefault();
                _context.chargeResponses.Remove(chargeresponse);
                _context.SaveChanges();
            }
            return View();
        }
        #endregion
        #region Admin Dashboard

        [Authorize]
        public IActionResult Dashboard()
        {
            ViewBag.CustomerCount = _userManager.Users.Where(x => x.Status == true).ToList().Count();
            ViewBag.InvoiceCount = _context.invoices.Where(x => x.Status == "Payment Captured").ToList().Count();
            ViewBag.ChangeCardCount = _context.changeCardInfos.ToList().Count();
            ViewBag.SubscriptionCount = _context.subscriptions.Where(x=>x.Status==true).ToList().Count();
            return View();
        }
        //Customer Section
        [Authorize]
        public IActionResult ViewCustomer()
        {
            var users = (from um in _context.Users 
                         join sub in _context.subscriptions on um.SubscribeID equals sub.SubscriptionId into ps
                         from sub in ps.DefaultIfEmpty()  
                     select new UserInfoDTO
                     {
                         Id = um.Id,
                         FullName = um.FullName,
                         Email = um.Email,
                         PhoneNumber = um.PhoneNumber,
                         Country = um.Country,
                         City = um.City,
                         Currency = um.Currency,
                         SubscribeName = sub.Name + " " + "-" + " " +"(" + sub.Amount + ")",
                         SubscribeID= um.SubscribeID,
                         Status = um.Status,
                     });
            return View(users);
        }
        public IActionResult AddCustomer()
        {
            ViewBag.SubscriptionList = _context.subscriptions.Select(x => new SelectListItem { Value = x.SubscriptionId.ToString(), Text = x.Name+ " " + "-"+" "+x.Amount }); 
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> AddCustomer(ApplicationUser applicationUser)
        {
            //Save data to tap side
            var countrycode = "";
            var currencycode = "";
            if (applicationUser.Country == "Bahrain")
            {
                countrycode = "+973";
                currencycode = "BHD";
            }
            else if (applicationUser.Country == "KSA")
            {
                countrycode = "+966";
                currencycode = "SAR";
            }
            else if (applicationUser.Country == "Kuwait")
            {
                countrycode = "+965";
                currencycode = "KWD";
            }
            else if (applicationUser.Country == "UAE")
            {
                countrycode = "+971";
                currencycode = "AED";
            }
            else if (applicationUser.Country == "Qatar")
            {
                countrycode = "+974";
                currencycode = "QAR";
            }
            else if (applicationUser.Country == "Oman")
            {
                countrycode = "+968";
                currencycode = "OMR";
            }
            applicationUser.Currency = currencycode;
            // save data to database
            applicationUser.FullName = applicationUser.FullName;
            applicationUser.Email = applicationUser.UserName;
            applicationUser.Status = true; 
            applicationUser.UserType = "Customer"; 
            applicationUser.EmailConfirmed = true; 
            applicationUser.PhoneNumberConfirmed = true; 
            applicationUser.Password = applicationUser.Password;
            applicationUser.Tap_CustomerID =null;
            await _userStore.SetUserNameAsync(applicationUser, applicationUser.Email, CancellationToken.None);
            if(applicationUser.Password != null)
            {
                ViewBag.SubscriptionList = _context.subscriptions.Select(x => new SelectListItem { Value = x.SubscriptionId.ToString(), Text = x.Name + " " + "-" + " " + x.Amount });
                ModelState.AddModelError(string.Empty, "Please Enter The Password...!");
            }
            var subscriptions = _context.subscriptions.Where(x => x.SubscriptionId == applicationUser.SubscribeID).FirstOrDefault();
            if (subscriptions.Countries == "Bahrain")
            {
                applicationUser.PublicKey = BHD_Public_Key;
                applicationUser.SecertKey = BHD_Test_Key;
            }
            else if (subscriptions.Countries == "KSA")
            {
                applicationUser.PublicKey = KSA_Public_Key;
                applicationUser.SecertKey = KSA_Test_Key;
            }
            var result = await _userManager.CreateAsync(applicationUser, applicationUser.Password);
            if(result.Succeeded)
            {
                int max_invoice_id = _context.invoices.Max(x => x.InvoiceId);
                string max_user_id = _context.Users.Where(x=>x.Email == applicationUser.Email).Select(x=>x.Id).FirstOrDefault();
                //Create Invoice
               // var users = GetCurrentUserAsync().Result;
                var Amount = subscriptions.Amount;
                int days = DateTime.DaysInMonth(DateTime.Now.Year, DateTime.Now.Month);
                int finalamount = 0;
                if (applicationUser.Frequency == "DAILY")
                {
                    finalamount = Convert.ToInt32(subscriptions.Amount) / days;
                }
                else if (applicationUser.Frequency == "WEEKLY")
                {
                    finalamount = Convert.ToInt32(subscriptions.Amount) / 4;
                }
                else if (applicationUser.Frequency == "MONTHLY")
                {
                    finalamount = Convert.ToInt32(subscriptions.Amount) / 1;
                }
                else if (applicationUser.Frequency == "QUARTERLY")
                {
                    finalamount = (Convert.ToInt32(subscriptions.Amount) * 3) / 1;
                }
                else if (applicationUser.Frequency == "HALFYEARLY")
                {
                    finalamount = (Convert.ToInt32(subscriptions.Amount) * 6) / 1;
                }
                else if (applicationUser.Frequency == "YEARLY")
                {
                    var discount_amount = (Convert.ToInt32(subscriptions.Amount) / 100) * 10;
                    finalamount = (Convert.ToInt32(subscriptions.Amount) - discount_amount) * 12;
                }
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

                body = body.Replace("{InvocieStatus}", "Un-Paid");
                body = body.Replace("{InvoiceID}", "Inv" + "233");
                body = body.Replace("{InvoiceAmount}", "23");


                body = body.Replace("{User_Name}", applicationUser.FullName);
                body = body.Replace("{User_Email}", applicationUser.Email);
                body = body.Replace("{User_Country}", applicationUser.Country);
                body = body.Replace("{User_Phone}", applicationUser.PhoneNumber);


                body = body.Replace("{SubscriptionName}", subscriptions.Name);
                body = body.Replace("{SubscriptionPeriod}", applicationUser.Frequency);
                body = body.Replace("{VAT}", "0.00");
                body = body.Replace("{SetupFee}", subscriptions.SetupFee);
                int amount = Convert.ToInt32(finalamount) + Convert.ToInt32(subscriptions.SetupFee);
                body = body.Replace("{Total}", amount.ToString());
                body = body.Replace("{SubscriptionAmount}", finalamount.ToString());

                var renderer = new ChromePdfRenderer();
                // Many rendering options to use to customize!
                renderer.RenderingOptions.SetCustomPaperSizeInInches(6.9, 12);
                renderer.RenderingOptions.PaperOrientation = IronPdf.Rendering.PdfPaperOrientation.Portrait;
                renderer.RenderingOptions.Title = "My PDF Document Name";
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
                var callbackUrl =  @Url.Action("Subscription", "Home" ,new { id = applicationUser.SubscribeID, link= "Yes", userid = max_user_id });
                var websiteurl  =  HtmlEncoder.Default.Encode(RedirectURL + callbackUrl);
                _ = _emailSender.SendEmailWithFIle(bytes, applicationUser.Email, "Un-Paid Invoice", "Hi..! <br /> Your Tamarran Credentials is here. <br /> Username: "+ applicationUser.UserName+" and <br /> Password: "+applicationUser.Password+" <br /> Payment URL: "+ websiteurl + "");
                var remove_invoice = _context.invoices.Where(x => x.InvoiceId == max_invoice_id).FirstOrDefault();
                _context.invoices.Remove(remove_invoice);
                _context.SaveChanges();
                return RedirectToAction("ViewCustomer", "Home");
            }
            foreach (var error in result.Errors)
            {
                ViewBag.SubscriptionList = _context.subscriptions.Select(x => new SelectListItem { Value = x.SubscriptionId.ToString(), Text = x.Name + " " + "-" + " " + x.Amount });
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return View();
        }
        [HttpPost]
        public ActionResult DeleteCustomer(string id)
        {
            var result = _context.Users.Where(x => x.Id == id).FirstOrDefault();
            result.Status = false;
            if (result != null)
            {
                _context.Update(result);
                _context.SaveChanges();
            }
            return RedirectToAction("ViewCustomer", "Home");
        }
        public async Task<IActionResult> InActiveUser(string id)
        {
            var subscriptions = _context.Users.Where(x => x.Id == id).FirstOrDefault();
            await _userManager.UpdateSecurityStampAsync(subscriptions);
            subscriptions.Status = false;
            _context.Users.Update(subscriptions);
            _context.SaveChanges();
            return RedirectToAction("ViewCustomer", "Home");
        }
        public IActionResult ActiveUser(string id) 
        {
            var subscriptions = _context.Users.Where(x => x.Id == id).FirstOrDefault();
            subscriptions.Status = true;
            _context.Users.Update(subscriptions);
            _context.SaveChanges();
            return RedirectToAction("ViewCustomer", "Home");
        }
        public ActionResult DeleteInvoice(int id, string userid, string status) 
        {
            var result = _context.invoices.Where(x => x.InvoiceId == id && x.UserId == userid).FirstOrDefault();
            _context.invoices.Remove(result);
            _context.SaveChanges();
            return RedirectToAction("CreateInvoice", "Home", new { PaymentStatus = "All" });
        }
        //Subscription Section
        [Authorize]
        public IActionResult Viewsubscription()
        {
            var subscriptions = _context.subscriptions.ToList();
            return View(subscriptions);
        }
        public ActionResult Deletesubscription(string userId) 
        {
            var result = _context.subscriptions.Where(x => x.SubscriptionId ==Convert.ToInt32(userId)).FirstOrDefault();
            result.Status = false;
            if (result != null)
            {
                _context.Update(result);
                _context.SaveChanges();
            }
            return RedirectToAction("Viewsubscription", "Home");
        }
        public IActionResult Addsubscription()
        {
            return View();
        }
        [HttpPost]
        public IActionResult Addsubscription(Subscriptions subscription)
        {
            if(ModelState.IsValid)
            {
                subscription.CreatedDate = DateTime.Now;
                subscription.Status = true;
                subscription.Frequency = "MONTHLY";
                _context.subscriptions.Add(subscription);
                _context.SaveChanges();
                return RedirectToAction("Viewsubscription", "Home");
            }
            else
            {
                return View(subscription);
            }
        }
        public IActionResult Editsubscription(string userId)
        {
            var subscriptions = _context.subscriptions.Where(x => x.Status == true && x.SubscriptionId ==Convert.ToInt32(userId)).FirstOrDefault();
            return View(subscriptions);
        }
        [HttpPost]  
        public IActionResult Editsubscription(Subscriptions subscription) 
        {
            if (ModelState.IsValid)
            {
                subscription.Status = true;
                _context.subscriptions.Update(subscription);
                _context.SaveChanges();
                return RedirectToAction("Viewsubscription", "Home");
            }
            else
            {
                return View(subscription);
            }
        }
        public IActionResult InActiveSubscription(int id)
        {
            var subscriptions = _context.subscriptions.Where(x => x.SubscriptionId == id).FirstOrDefault();
            subscriptions.Status = false;
            _context.subscriptions.Update(subscriptions);
            _context.SaveChanges();
            return RedirectToAction("Viewsubscription", "Home");
        }
        public IActionResult ActiveSubscription(int id)
        {
            var subscriptions = _context.subscriptions.Where(x => x.SubscriptionId == id).FirstOrDefault();
            subscriptions.Status = true;
            _context.subscriptions.Update(subscriptions);
            _context.SaveChanges();
            return RedirectToAction("Viewsubscription", "Home");
        }
        public IActionResult GetAllCharges()
        {
            var users = (from cr in _context.invoices
                         join um in _context.Users on cr.UserId  equals um.Id
                         where cr.Status == "Payment Captured"
                         select new ChargeListDTO
                         {
                             Tap_CustomerID = um.Tap_CustomerID,
                             UserId = um.Id,
                             FullName = um.FullName,
                             Email = um.Email,
                             Country = um.Country,
                             City = um.City,
                             currency = um.Currency,
                             ChargeId = cr.ChargeId,
                             status = cr.Status,
                             PaymentDate = cr.AddedDate,
                             amount = cr.SubscriptionAmount
                         }).ToList();
            return View(users);
        }
        public ActionResult UnSubscribeSubscription(string id) 
        {
            var userinfo = _context.Users.Where(x => x.Id == id).FirstOrDefault();
            userinfo.SubscribeID = 0;
            _context.Users.Update(userinfo);
            _context.SaveChanges();

            // Send Email
            _ = _emailSender.SendEmailAsync(userinfo.Email, "Un-Subscribe", "You Un-Subscribe the subscription successfully. Thank you for choosing us.");
            return RedirectToAction("ViewGYMCustomer");
        }
        //List Section
        public ActionResult ViewSubinfo()
        {
            var users = (from um in _context.Users
                         join sub in _context.subscriptions on um.SubscribeID equals sub.SubscriptionId into ps
                         from sub in ps.DefaultIfEmpty()
                         where um.Id == GetCurrentUserAsync().Result.Id
                         select new UserInfoDTO
                         {
                             Id = um.Id,
                             FullName = um.FullName,
                             Email = um.Email,
                             PhoneNumber = um.PhoneNumber,
                             Country = um.Country,
                             City = um.City,
                             Currency = um.Currency,
                             SubscribeName = sub.Name + " " + "-" + " " + "(" + sub.Amount + ")",
                             SubscribeID = um.SubscribeID,
                             Status = um.Status,
                         });
            return View(users);
        }
        public async Task<IActionResult> ViewInvoice(string id, int sub_id) 
        {
            //Get Charge Detail
            var client_ChargeDetail = new HttpClient();
            var request_ChargeDetail = new HttpRequestMessage(HttpMethod.Get, "https://api.tap.company/v2/charges/" + id); 
            request_ChargeDetail.Headers.Add("Authorization", "Bearer " + GetCurrentUserAsync().Result.SecertKey);
            request_ChargeDetail.Headers.Add("accept", "application/json");
            var response_ChargeDetail = await client_ChargeDetail.SendAsync(request_ChargeDetail);
            var result_ChargeDetail = await response_ChargeDetail.Content.ReadAsStringAsync();
            ChargeDetail Deserialized_savecard = JsonConvert.DeserializeObject<ChargeDetail>(result_ChargeDetail);
            var subscription_info = _context.subscriptions.Where(x => x.SubscriptionId == sub_id).FirstOrDefault();
            Deserialized_savecard.Subscriptions = subscription_info;
            ViewBag.Frequency = GetCurrentUserAsync().Result.Frequency;
            return View(Deserialized_savecard);
        }
        public IActionResult ShowInvoice(string PaymentStatus)
        {
            if (PaymentStatus == "All")
            {
                var invoices = _context.invoices.Where(x => x.UserId == GetCurrentUserAsync().Result.Id).OrderByDescending(x => x.InvoiceStartDate).ToList();
                return View(invoices);
            }
            else
            {
                var invoices = _context.invoices.Where(x => x.UserId == GetCurrentUserAsync().Result.Id && x.Status == PaymentStatus).OrderByDescending(x => x.InvoiceStartDate).ToList();
                return View(invoices);
            }
        }
        #endregion
        #region Gym Customer Registration
        public IActionResult AddGymCustomer(int id)
        {
            ViewBag.SubscriptionList = id;
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> AddGymCustomer(ApplicationUser applicationUser)
        {
            var countrycode = "";
            var currencycode = "";
            if (applicationUser.Country == "Bahrain")
            {
                countrycode = "+973";
                currencycode = "BHD";
            }
            else if (applicationUser.Country == "KSA")
            {
                countrycode = "+966";
                currencycode = "SAR";
            }
            else if (applicationUser.Country == "Kuwait")
            {
                countrycode = "+965";
                currencycode = "KWD";
            }
            else if (applicationUser.Country == "UAE")
            {
                countrycode = "+971";
                currencycode = "AED";
            }
            else if (applicationUser.Country == "Qatar")
            {
                countrycode = "+974";
                currencycode = "QAR";
            }
            else if (applicationUser.Country == "Oman")
            {
                countrycode = "+968";
                currencycode = "OMR";
            }
            applicationUser.Currency = currencycode;
            var selectsub_country = _context.subscriptions.Where(x => x.SubscriptionId == applicationUser.SubscribeID).Select(x => x.Countries).FirstOrDefault();
            // save data to database
            var subid = applicationUser.SubscribeID;
            applicationUser.Email = applicationUser.UserName;
            applicationUser.Status = true;
            applicationUser.UserType = "Customer";
            applicationUser.EmailConfirmed = true;
            applicationUser.PhoneNumberConfirmed = true;
            applicationUser.Password = applicationUser.Password;
            applicationUser.Tap_CustomerID = null;
            applicationUser.SubscribeID = 0;
            await _userStore.SetUserNameAsync(applicationUser, applicationUser.Email, CancellationToken.None);
            if (applicationUser.Password == null)
            {
                ViewBag.SubscriptionList = _context.subscriptions.Select(x => new SelectListItem { Value = x.SubscriptionId.ToString(), Text = x.Name + " " + "-" + " " + x.Amount });
                ModelState.AddModelError(string.Empty, "Please Enter The Password...!");
            }

            if (selectsub_country == "Bahrain")
            {
                applicationUser.PublicKey = BHD_Public_Key;
                applicationUser.SecertKey = BHD_Test_Key;
            }
            else if (selectsub_country == "KSA")
            {
                applicationUser.PublicKey = KSA_Public_Key;
                applicationUser.SecertKey = KSA_Test_Key;
            }
            var result = await _userManager.CreateAsync(applicationUser, applicationUser.Password);
            if (result.Succeeded)
            {
                var istrue = await _signInManager.PasswordSignInAsync(applicationUser.UserName.ToString(), applicationUser.Password, true, lockoutOnFailure: true);
                return RedirectToAction("Subscription", "Home", new { id = subid });
            }
            foreach (var error in result.Errors)
            {
                ViewBag.SubscriptionList = _context.subscriptions.Select(x => new SelectListItem { Value = x.SubscriptionId.ToString(), Text = x.Name + " " + "-" + " " + x.Amount });
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return View();
        }
        [Authorize]
        public IActionResult ViewGYMCustomer()
        {
            var users = (from um in _context.Users
                         join sub in _context.subscriptions on um.SubscribeID equals sub.SubscriptionId into ps
                         from sub in ps.DefaultIfEmpty()
                         where um.Id == GetCurrentUserAsync().Result.Id
                         select new UserInfoDTO
                         {
                             Id = um.Id,
                             FullName = um.FullName,
                             Email = um.Email,
                             PhoneNumber = um.PhoneNumber,
                             Country = um.Country,
                             City = um.City,
                             Currency = um.Currency,
                             SubscribeName = sub.Name + " " + "-" + " " + "(" + sub.Amount + ")",
                             SubscribeID = um.SubscribeID,
                             Status = um.Status,
                             PaymentSource = um.PaymentSource,
                             GYMName = um.GYMName
                         });
            return View(users);
        }
        #endregion

    }
}