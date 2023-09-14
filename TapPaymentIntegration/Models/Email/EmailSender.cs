using Microsoft.AspNetCore.Identity.UI.Services;
using System.Net;
using System.Net.Mail;

namespace TapPaymentIntegration.Models.Email
{
    public class EmailSender : IEmailSender
    {
        
        public async Task SendEmailWithFIle(byte[]? bytesArray,string emails, string subject, string message)
        {
            try
            {
                SmtpClient client = new SmtpClient();
                client.Host = "email-smtp.ap-south-1.amazonaws.com";
                client.Port = 587;
                client.UseDefaultCredentials = false;
                client.EnableSsl = true;
                client.DeliveryMethod = SmtpDeliveryMethod.Network;
                client.Credentials = new NetworkCredential("AKIA4A4DJ4EYADB5UFHL", "BCUESNHERw+nBq1gi+cjB+F5myPhCcIwfQKl59X0uFdK");
                client.DeliveryMethod = SmtpDeliveryMethod.Network;
                MailMessage mail = new MailMessage();
                mail.From = new MailAddress("accounts@tamarran.com", "Tamarran");
                foreach (var address in emails.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries))
                {
                    mail.To.Add(new MailAddress(address));
                }
                mail.Subject = subject;
                mail.IsBodyHtml = true;
                mail.Body = message;
                mail.Attachments.Add(new Attachment(new MemoryStream(bytesArray), "Invoice.pdf"));
                client.Send(mail);
            }
            catch (Exception ex)
            {
                // log exception
            }
            await Task.CompletedTask;
        } 
        public async Task SendEmailAsync(string emails, string subject, string message)
        {
            try
            {
                SmtpClient client = new SmtpClient();
                client.Host = "email-smtp.ap-south-1.amazonaws.com";
                client.Port = 587;
                client.UseDefaultCredentials = false;
                client.EnableSsl = true;
                client.DeliveryMethod = SmtpDeliveryMethod.Network;
                client.Credentials = new NetworkCredential("AKIA4A4DJ4EYADB5UFHL", "BCUESNHERw+nBq1gi+cjB+F5myPhCcIwfQKl59X0uFdK");
                client.DeliveryMethod = SmtpDeliveryMethod.Network;
                MailMessage mail = new MailMessage();
                mail.From = new MailAddress("accounts@tamarran.com", "Tamarran");
                foreach (var address in emails.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries))
                {
                    mail.To.Add(new MailAddress(address));
                }
                mail.Subject = subject;
                mail.IsBodyHtml = true;
                mail.Body = message;
                client.Send(mail);
            }
            catch (Exception ex)
            {
                // log exception
            }
            await Task.CompletedTask;
        }
    }
}
