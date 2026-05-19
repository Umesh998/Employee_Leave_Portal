// =============================================================================
// Employee_Leave_Portal — EmailService
// File: Services/EmailService.cs
// =============================================================================

using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Employee_Leave_Portal.Services
{
    public interface IEmailService
    {
        Task SendAsync(string toEmail, string toName, string subject, string htmlBody);
    }

    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;

        public EmailService(IConfiguration config) => _config = config;

        public async Task SendAsync(string toEmail, string toName, string subject, string htmlBody)
        {
            var smtp = _config["Email:SmtpHost"];
            var port = int.Parse(_config["Email:SmtpPort"] ?? "587");
            var user = _config["Email:Username"];
            var pass = _config["Email:Password"];
            var fromName = _config["Email:FromName"] ?? "Employee Leave Portal";

            using var client = new SmtpClient(smtp, port)
            {
                Credentials = new NetworkCredential(user, pass),
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };

            var message = new MailMessage
            {
                From = new MailAddress(user!, fromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };

            message.To.Add(new MailAddress(toEmail, toName));

            await client.SendMailAsync(message);
        }
    }
}
