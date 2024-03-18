using System;
using Google.Api.Gax.ResourceNames;
using Google.Cloud.RecaptchaEnterprise.V1;

namespace TapPaymentIntegration.Models
{
    public static class Constants
    {

#if !DEBUG
        public static readonly string RedirectURL = "https://billing.tamarran.com";  //don't put / in the end
#endif
#if DEBUG
        //public static readonly string RedirectURL = "https://test.softsolutionlogix.com";  //don't put / in the end
        public static readonly string RedirectURL = "https://localhost:7279";  //don't put / in the end
#endif
        public const string SubscriptionErrorMessage = "subscription is In-Active";

        #region EMAIL SENDING

        public static readonly string HOST = "email-smtp.ap-south-1.amazonaws.com";
        public static readonly int PORT = 587;
        public static readonly string NETWORKCREDENTIALUSERNAME = "AKIA4A4DJ4EYADB5UFHL";
        public static readonly string NETWORKCREDENTIALPASSWORD = "g/ncZrQ16rofq0SlifE9TB1gX4r4DsVIIhPYM2/u";
        public static readonly string MAINEMAILADDRESS = "accounts@tamarran.com";
        public static readonly string BCC = "ali.zayer@tamarran.com";
        public static readonly string MAINDISPLAYNAME = "Tamarran";

        #endregion

        #region  LIVE KEYS
#if !DEBUG
        //// Baharin
        //public static readonly string BHD_Public_Key = "pk_test_7sAiZNXvdpKax26RuJMwbIen";
        //public static readonly string BHD_Test_Key = "sk_test_Tgoy8HbxdQ40l6Ea9SIDci7B";
        //public static readonly string BHD_Merchant_Key = "";
        ////KSA 
        //public static readonly string KSA_Public_Key = "pk_test_j3yKfvbxws8khDpFQOX5JeWc";
        //public static readonly string KSA_Test_Key = "sk_test_1SU5woL8vZe6JXrBHipQu9Dn";
        //public static readonly string KSA_Merchant_Key = "22116401";

        // Baharin
        public static readonly string BHD_Public_Key = "pk_live_7MqbnXVzGkRBaO3KWEmwN8i1";
        public static readonly string BHD_Test_Key = "sk_live_85POWSybstdevAiMxYaGHNp3";
        public static readonly string BHD_Merchant_Key = "";
        //KSA   
        public static readonly string KSA_Public_Key = "pk_live_MWDV5szwGbxeUBdHnJZLk9S2";
        public static readonly string KSA_Test_Key = "sk_live_VDJ1UxM2Arq6ONbz9ptGXhoj";
        public static readonly string KSA_Merchant_Key = "22116401";
#endif
        #endregion

        #region LOCALHOST TESTING KEYS
#if DEBUG
    //Baharin
    public static readonly string BHD_Public_Key = "pk_test_7sAiZNXvdpKax26RuJMwbIen";
    public static readonly string BHD_Test_Key = "sk_test_Tgoy8HbxdQ40l6Ea9SIDci7B";
    public static readonly string BHD_Merchant_Key = "";
    //KSA 
    public static readonly string KSA_Public_Key = "pk_test_j3yKfvbxws8khDpFQOX5JeWc";
    public static readonly string KSA_Test_Key = "sk_test_1SU5woL8vZe6JXrBHipQu9Dn";
    public static readonly string KSA_Merchant_Key = "22116401";
#endif
        #endregion

        #region GOOGLE RECAPTCHA CODE

        public static readonly string PROJECTID = "billing-tamarran-1710522152896";

        #endregion
    }


    public static class CreateAssessmentSample
    {
        // Create an assessment to analyze the risk of a UI action.
        // projectID: Your Google Cloud Project ID.
        // recaptchaKey: The reCAPTCHA key associated with the site/app
        // token: The generated token obtained from the client.
        // recaptchaAction: Action name corresponding to the token.
        public static bool createAssessment(string token, string recaptchaAction)
        {
            // Create the reCAPTCHA client.
            // TODO: Cache the client generation code (recommended) or call client.close() before exiting the method.
            RecaptchaEnterpriseServiceClient client = RecaptchaEnterpriseServiceClient.Create();

            ProjectName projectName = new ProjectName(Constants.PROJECTID);

            // Build the assessment request.
            CreateAssessmentRequest createAssessmentRequest = new CreateAssessmentRequest()
            {
                Assessment = new Assessment()
                {
                    // Set the properties of the event to be tracked.
                    Event = new Event()
                    {
                        SiteKey = Constants.PROJECTID,
                        Token = token,
                        ExpectedAction = recaptchaAction
                    },
                },
                ParentAsProjectName = projectName
            };

            Assessment response = client.CreateAssessment(createAssessmentRequest);

            // Check if the token is valid.
            if (response.TokenProperties.Valid == false)
            {
                return false;
            }

            // Check if the expected action was executed.
            if (response.TokenProperties.Action != recaptchaAction)
            {
                return false;
            }

            // Get the risk score and the reason(s).
            // For more information on interpreting the assessment, see:
            // https://cloud.google.com/recaptcha-enterprise/docs/interpret-assessment
            //System.Console.WriteLine("The reCAPTCHA score is: " + ((decimal)response.RiskAnalysis.Score));

            //foreach (RiskAnalysis.Types.ClassificationReason reason in response.RiskAnalysis.Reasons)
            //{
            //    System.Console.WriteLine(reason.ToString());
            //}

            return true;
        }
    }
}
