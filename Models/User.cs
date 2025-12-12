using Google.Cloud.Firestore;
using System;
using System.ComponentModel.DataAnnotations;

namespace Leave2Day.Models
{
    [FirestoreData]
    public class User
    {
        [FirestoreDocumentId]
        public string id { get; set; } // User ID

        [FirestoreProperty("email")]
        [Required]
        public string email { get; set; } // Email address

        [FirestoreProperty("firstName")]
        [Required]
        public string firstName { get; set; } // First name

        [FirestoreProperty("lastName")]
        [Required]
        public string lastName { get; set; } // Last name

        [FirestoreProperty("phoneNumber")]
        [Required]
        [Display(Name = "Phone Number")]
        public string phoneNumber { get; set; } // Phone number

        [FirestoreProperty]
        [Required]
        public string department { get; set; } // Department

        [FirestoreProperty("role")]
        public string RoleString { get; set; } // Role as string from Firestore

        [FirestoreProperty]
        public string? ProofDocumentLink { get; set; } // Document link

        // Computed role with ID-based fallback
        public UserRole role
        {
            get
            {
                // Try to parse from stored role string
                if (!string.IsNullOrEmpty(RoleString) &&
                    Enum.TryParse<UserRole>(RoleString, true, out var parsed))
                    return parsed;

                // Fallback: determine role from user ID prefix
                if (!string.IsNullOrEmpty(id))
                {
                    var lower = id.ToLower();
                    if (lower.StartsWith("sup")) return UserRole.Supervisor;
                    if (lower.StartsWith("hod")) return UserRole.HOD;
                    if (lower.StartsWith("hr")) return UserRole.HR;
                }

                return UserRole.Employee; // Default role
            }
            set
            {
                RoleString = value.ToString(); // Set role string when role is assigned
            }
        }

        [FirestoreProperty]
        public string supervisorId { get; set; } // Supervisor's ID

        [FirestoreProperty]
        public string hodId { get; set; } // HOD's ID

        [FirestoreProperty("createdAt")]
        public DateTime? createdAt { get; set; } // Account creation date

        [FirestoreProperty]
        public bool IsActive { get; set; } // Active status

        [FirestoreProperty("leaveBalance")]
        public LeaveBalance LeaveBalance { get; set; } = new LeaveBalance(); // Leave balances

        // Computed properties
        public string FullName => $"{firstName} {lastName}"; // Full name

        // Remaining leave calculations
        public int RemainingAnnualLeave => LeaveBalance.Annual - LeaveBalance.UsedAnnual;
        public int RemainingSickLeave => LeaveBalance.Sick - LeaveBalance.UsedSick;
        public int RemainingMaternityLeave => LeaveBalance.Maternity - LeaveBalance.UsedMaternity;
        public int RemainingPaternityLeave => LeaveBalance.Paternity - LeaveBalance.UsedPaternity;
        public int RemainingEmergencyLeave => LeaveBalance.Emergency - LeaveBalance.UsedEmergency;
    }

    [FirestoreData]
    public class LeaveBalance
    {
        // Total allocated leave days
        [FirestoreProperty("annual")]
        public int Annual { get; set; } = 21; // Annual leave

        [FirestoreProperty("sick")]
        public int Sick { get; set; } = 15; // Sick leave

        [FirestoreProperty("emergency")]
        public int Emergency { get; set; } = 5; // Emergency leave

        [FirestoreProperty("maternity")]
        public int Maternity { get; set; } = 90; // Maternity leave

        [FirestoreProperty("paternity")]
        public int Paternity { get; set; } = 14; // Paternity leave

        // Used leave days
        [FirestoreProperty("UsedAnnual")]
        public int UsedAnnual { get; set; }

        [FirestoreProperty("UsedSick")]
        public int UsedSick { get; set; }

        [FirestoreProperty("usedEmergency")]
        public int UsedEmergency { get; set; }

        [FirestoreProperty("usedMaternity")]
        public int UsedMaternity { get; set; }

        [FirestoreProperty("usedPaternity")]
        public int UsedPaternity { get; set; }
    }

    public enum UserRole
    {
        Employee = 1,    // Regular employee
        Supervisor = 2,  // Team supervisor
        HOD = 3,         // Head of Department
        HR = 4           // Human Resources
    }
}