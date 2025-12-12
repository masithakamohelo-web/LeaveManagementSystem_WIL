using System;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Leave2Day.Services
{
    public class EmailSettings
    {
        public string SmtpServer { get; set; } = "smtp.gmail.com";
        public int SmtpPort { get; set; } = 587;
        public string SmtpUsername { get; set; } = "";
        public string SmtpPassword { get; set; } = "";
        public string FromEmail { get; set; } = "";
        public string FromName { get; set; } = "";
    }

    public class EmailService
    {
        private readonly EmailSettings _settings;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IOptions<EmailSettings> options, ILogger<EmailService> logger)
        {
            _settings = options.Value;
            _logger = logger;
        }

        private async Task SendEmailInternalAsync(string toEmail, string toName, string subject, string htmlBody)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromEmail));
            message.To.Add(new MailboxAddress(string.IsNullOrWhiteSpace(toName) ? toEmail : toName, toEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
            message.Body = bodyBuilder.ToMessageBody();

            using var smtp = new SmtpClient();

            try
            {
                // Connect & authenticate
                await smtp.ConnectAsync(_settings.SmtpServer, _settings.SmtpPort, SecureSocketOptions.StartTls);
                if (!string.IsNullOrEmpty(_settings.SmtpUsername))
                {
                    await smtp.AuthenticateAsync(_settings.SmtpUsername, _settings.SmtpPassword);
                }

                await smtp.SendAsync(message);
                await smtp.DisconnectAsync(true);

                _logger.LogInformation("Email sent to {Email} (subject: {Subject})", toEmail, subject);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {Email}", toEmail);
                throw; // bubble up so controllers can log or handle
            }
        }

        // Convenience methods used by controllers:
        public Task SendApplicantConfirmationAsync(string email, string fullName, string applicationId)
        {
            var subject = "Leave Application Received";
            var html = $@"
                <p>Dear {fullName},</p>
                <p>Your leave application (ID: {applicationId}) has been successfully submitted and is pending approval.</p>
                <p>Details:<br/>
                <strong>Application ID:</strong> {applicationId}</p>
                <p>Regards,<br/>Leave Management System</p>";
            return SendEmailInternalAsync(email, fullName, subject, html);
        }

        public Task SendSupervisorAwaitingApprovalAsync(string email, string supervisorName, string applicationId)
        {
            var subject = "Leave Application Awaiting Your Approval";
            var html = $@"
                <p>Dear {supervisorName},</p>
                <p>A leave application (ID: {applicationId}) has been submitted and requires your approval.</p>
                <p>Please log in to the system to review and take action.</p>
                <p>Regards,<br/>Leave Management System</p>";
            return SendEmailInternalAsync(email, supervisorName, subject, html);
        }

        public Task SendHodAwaitingApprovalAsync(string email, string hodName, string applicationId)
        {
            var subject = "Leave Application Awaiting HOD Approval";
            var html = $@"
                <p>Dear {hodName},</p>
                <p>An application (ID: {applicationId}) has been approved by the supervisor and awaits your approval.</p>
                <p>Regards,<br/>Leave Management System</p>";
            return SendEmailInternalAsync(email, hodName, subject, html);
        }

        public Task SendHrRecordAsync(string email, string hrName, string applicationId)
        {
            var subject = "Leave Application Approved - Please Record";
            var html = $@"
                <p>Dear {hrName},</p>
                <p>Application (ID: {applicationId}) has been fully approved and is ready to be recorded by HR.</p>
                <p>Regards,<br/>Leave Management System</p>";
            return SendEmailInternalAsync(email, hrName, subject, html);
        }
    }
}
