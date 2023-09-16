using Amazon.SimpleEmail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Net.Http.Headers;
using TapPaymentIntegration.Areas.Identity.Data;
using TapPaymentIntegration.Data;
using TapPaymentIntegration.Models.Card;
using TapPaymentIntegration.Models.Email;
using TapPaymentIntegration.Models.InvoiceDTO;
using TapPaymentIntegration.Models.PaymentDTO;
using TapPaymentIntegration.Models.UserDTO;

namespace TapPaymentIntegration.Controllers
{
    public class TapPaymentController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private TapPaymentIntegrationContext _context;
        private readonly IUserStore<ApplicationUser> _userStore;
        public readonly string RedirectURL = "https://localhost:7279/" + "TapPayment/ChangeCardResponse";
        private Task<ApplicationUser> GetCurrentUserAsync() => _userManager.GetUserAsync(HttpContext.User);
        public TapPaymentController(ILogger<HomeController> logger, SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager, TapPaymentIntegrationContext context, IUserStore<ApplicationUser> userStore)
        {
            _logger = logger;
            _signInManager = signInManager;
            _userManager = userManager;
            _context = context;
            _userStore = userStore;
        }
        public async Task<IActionResult> Index(int id,string userid)
        {
            var userinfo = _context.Users.Where(x => x.Id == userid).FirstOrDefault();
            var invoice = _context.invoices.Where(x=>x.InvoiceId == id).FirstOrDefault();
            Random rnd = new Random();
            var TransNo ="Txn_" +  rnd.Next(10000000, 99999999);
            var OrderNo ="Ord_" +  rnd.Next(10000000, 99999999);
            var currency = userinfo.Currency;
            var amount = invoice.SubscriptionAmount;
            var description = invoice.Description;
            var countrycode = "";
            if (userinfo.Country == "Bahrain")
            {
                countrycode = "+973";
            }
            else if (userinfo.Country == "KSA")
            {
                countrycode = "+966";
            }
            else if (userinfo.Country == "Kuwait")
            {
                countrycode = "+965";
            }
            else if (userinfo.Country == "UAE")
            {
                countrycode = "+971";
            }
            else if (userinfo.Country == "Qatar")
            {
                countrycode = "+974";
            }
            else if (userinfo.Country == "Oman")
            {
                countrycode = "+968";
            }
            else
            {
                countrycode = "966";
            }
            Reference reference = new Reference();
            reference.transaction = TransNo;
            reference.order = OrderNo;

            Redirect redirect = new Redirect();
            redirect.url = "https://localhost:7279/Home/ChargeDone";

            Post post = new Post();
            post.url = "https://localhost:7279/Home/ChargeDone";

            Phone phone = new Phone();
            phone.number = userinfo.PhoneNumber;
            phone.country_code = countrycode;

            Customer customer = new Customer();
            customer.first_name = userinfo.FullName;
            customer.email = userinfo.Email;
            customer.phone = phone;

            Receipt receipt = new Receipt();
            receipt.sms = true;
            receipt.email = true;

            Metadata metadata = new Metadata();
            metadata.udf1 = "Metadata 1";

            Source source = new Source();   
            source.id = "src_all";

            Merchant merchant = new Merchant();
            merchant.id = "22116401";

            FillChargeModel fillChargeModel = new FillChargeModel();
            fillChargeModel.threeDSecure = true;
            fillChargeModel.amount = amount;
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
            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tap.company/v2/charges");
            request.Headers.Add("Authorization", "Bearer sk_test_1SU5woL8vZe6JXrBHipQu9Dn");
            request.Headers.Add("accept", "application/json");
            var content = new StringContent(jsonmodel, null, "application/json");
            request.Content = content;
            var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            CreateCharge deserialized_CreateCharge = JsonConvert.DeserializeObject<CreateCharge>(body);
            ChargeResponse chargeResponse = new ChargeResponse
            {
                UserId = userid,
                ChargeId = deserialized_CreateCharge.id,
                amount = deserialized_CreateCharge.amount,
                currency = currency,
                status = deserialized_CreateCharge.status,
            };
            _context.chargeResponses.Add(chargeResponse);
            _context.SaveChanges();
            Invoice invoice_info = _context.invoices.Find(id);
            invoice_info.ChargeId = deserialized_CreateCharge.id;
            invoice_info.ChargeResponseId = _context.chargeResponses.Max(x => x.ChargeResponseId);
            _context.invoices.Update(invoice_info);
            _context.SaveChanges();
            return Redirect(deserialized_CreateCharge.transaction.url);
        }
        public ActionResult CustomePaymentDone()
        { 
            return View(); 
        }
        public ActionResult ChangeCard()
        {
            return View();
        }
        [HttpPost]
        public async Task<ActionResult> ChangeCard(string id)
        {
            var Token = Request.Form.Where(x => x.Key == "Token").FirstOrDefault().Value.ToString();
            if (Token != null)
            {
                //Get Loggedin User Info
                var userinfo = GetCurrentUserAsync().Result;
                var subinfo = _context.subscriptions.Where(x => x.SubscriptionId == userinfo.SubscribeID).FirstOrDefault();
                var countrycode = "";
                if (userinfo.Country == "Bahrain")
                {
                    countrycode = "+973";
                }
                else if (userinfo.Country == "KSA")
                {
                    countrycode = "+966";
                }
                else if (userinfo.Country == "Kuwait")
                {
                    countrycode = "+965";
                }
                else if (userinfo.Country == "UAE")
                {
                    countrycode = "+971";
                }
                else if (userinfo.Country == "Qatar")
                {
                    countrycode = "+974";
                }
                else if (userinfo.Country == "Oman")
                {
                    countrycode = "+968";
                }
                else
                {
                    countrycode = "966";
                }
                var client = new HttpClient();
                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tap.company/v2/card/verify");
                request.Headers.Add("authorization", "Bearer " + userinfo.SecertKey);
                request.Headers.Add("accept", "application/json");
                var content = new StringContent("{\"currency\":\""+ subinfo.Currency + "\",\"threeDSecure\":true,\"save_card\":true,\"metadata\":{\"udf1\":\"test1\",\"udf2\":\"test2\"},\"customer\":{\"first_name\":\""+userinfo.FullName+"\",\"middle_name\":\"\",\"last_name\":\"\",\"email\":\""+ userinfo.Email+ "\",\"phone\":{\"country_code\":\""+ countrycode + "\",\"number\":\""+userinfo.PhoneNumber+"\"}},\"source\":{\"id\":\""+ Token + "\"},\"redirect\":{\"url\":\""+ RedirectURL + "\"}}", null, "application/json");
                request.Content = content;
                var response = await client.SendAsync(request);
                var result_erify = await response.Content.ReadAsStringAsync();
                var Deserialized_Deserialized_savecard = JsonConvert.DeserializeObject<VerifyCard>(result_erify);
                HttpContext.Session.SetString("Verify_id", Deserialized_Deserialized_savecard.id);
                return Json(Deserialized_Deserialized_savecard.transaction.url);
            }
            return Json(false);
        }
        public async Task<ActionResult> ChangeCardResponse()
        {
            var userinfo = GetCurrentUserAsync().Result;
            var subinfo = _context.subscriptions.Where(x => x.SubscriptionId == userinfo.SubscribeID).FirstOrDefault();
            var verify_id = HttpContext.Session.GetString("Verify_id");
            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.tap.company/v2/card/verify/" + verify_id);
            request.Headers.Add("authorization", "Bearer " + userinfo.SecertKey);
            var collection = new List<KeyValuePair<string, string>>();
            collection.Add(new("{}", ""));
            var content = new FormUrlEncodedContent(collection);
            request.Content = content;
            var response = await client.SendAsync(request);
            var result  = await response.Content.ReadAsStringAsync();
            var Deserialized_savecard = JsonConvert.DeserializeObject<RetriveCardResponse>(result);
            //update user 
            userinfo.Tap_CustomerID = Deserialized_savecard.payment_agreement.contract.customer_id;
            userinfo.Tap_Card_ID = Deserialized_savecard.payment_agreement.contract.id;
            userinfo.Tap_Agreement_ID = Deserialized_savecard.payment_agreement.id;
            userinfo.PaymentSource = Deserialized_savecard.source.payment_method;
            _context.Users.Update(userinfo);
            _context.SaveChanges();
            return RedirectToAction("Dashboard", "Home");
        }
    }
}
