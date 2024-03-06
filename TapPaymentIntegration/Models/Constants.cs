namespace TapPaymentIntegration.Models
{
  public static class Constants
  {
#if !DEBUG
    public static readonly string RedirectURL = "https://tap.softsolutionlogix.com";
#endif
#if DEBUG
        public static readonly string RedirectURL = "https://localhost:7279";
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
    // Baharin
    public static readonly string BHD_Public_Key = "pk_test_7sAiZNXvdpKax26RuJMwbIen";
    public static readonly string BHD_Test_Key = "sk_test_Tgoy8HbxdQ40l6Ea9SIDci7B";
    public static readonly string BHD_Merchant_Key = "";
    //KSA 
    public static readonly string KSA_Public_Key = "pk_test_j3yKfvbxws8khDpFQOX5JeWc";
    public static readonly string KSA_Test_Key = "sk_test_1SU5woL8vZe6JXrBHipQu9Dn";
    public static readonly string KSA_Merchant_Key = "22116401";
#endif
#endregion
  }
}
