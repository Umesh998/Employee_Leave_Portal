// =============================================================================
// Employee_Leave_Portal — OtpService
// File: Services/OtpService.cs
// =============================================================================
//
// Stores OTPs in-memory with a 5-minute expiry.
// No database table needed — OTPs are short-lived.
// =============================================================================

using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Employee_Leave_Portal.Services
{
    public interface IOtpService
    {
        string GenerateOtp(string email);
        bool ValidateOtp(string email, string otp);
    }

    public class OtpService : IOtpService
    {
        private readonly ConcurrentDictionary<string, (string Otp, DateTime Expiry)> _store = new();

        /// <summary>Generates a 6-digit OTP, stores it with 5-min expiry, returns it.</summary>
        public string GenerateOtp(string email)
        {
            // Cryptographically random 6-digit OTP
            string otp = RandomNumberGenerator.GetInt32(100000, 999999).ToString();

            _store[email.ToLowerInvariant()] = (otp, DateTime.Now.AddMinutes(5));

            return otp;
        }

        /// <summary>Returns true if OTP matches and has not expired. Invalidates on success.</summary>
        public bool ValidateOtp(string email, string otp)
        {
            string key = email.ToLowerInvariant();

            if (!_store.TryGetValue(key, out var entry))
                return false;

            if (DateTime.Now > entry.Expiry)
            {
                _store.TryRemove(key, out _);
                return false;
            }

            if (entry.Otp != otp.Trim())
                return false;

            // Invalidate after successful use
            _store.TryRemove(key, out _);
            return true;
        }
    }
}
