using Google.Cloud.Firestore;
using Leave2Day.Models;
using System;

namespace Leave2Day.Converters
{
    // Converts LeaveType enum for Firestore storage
    public class LeaveTypeConverter : IFirestoreConverter<LeaveType>
    {
        // Convert Firestore string to LeaveType enum
        public LeaveType FromFirestore(object value)
        {
            if (value is string stringValue)
            {
                return stringValue.ToLower() switch
                {
                    "annual" => LeaveType.Annual,
                    "sick" => LeaveType.Sick,
                    "maternity" => LeaveType.Maternity,
                    "paternity" => LeaveType.Paternity,
                    "emergency" => LeaveType.Emergency,
                    _ => LeaveType.Annual // Default fallback
                };
            }
            return LeaveType.Annual;
        }

        // Convert LeaveType enum to Firestore string
        public object ToFirestore(LeaveType value)
        {
            return value.ToString();
        }
    }

    // Converts LeaveStatus enum for Firestore storage
    public class LeaveStatusConverter : IFirestoreConverter<LeaveStatus>
    {
        // Convert Firestore string to LeaveStatus enum
        public LeaveStatus FromFirestore(object value)
        {
            if (value is string stringValue)
            {
                // Normalize string for comparison
                var lowerValue = stringValue.ToLower().Replace(" ", "").Replace("_", "").Replace("-", "");

                return lowerValue switch
                {
                    "rejected" or "rejectedbysupervisor" => LeaveStatus.rejectedBySupervisor,
                    "rejectedbyhod" => LeaveStatus.rejectedByHod,
                    "approved" or "approvedbysupervisor" or "supervisorapproved" => LeaveStatus.approvedBySupervisor,
                    "approvedbyhod" or "hodapproved" => LeaveStatus.approvedByHod,
                    "recorded" => LeaveStatus.recorded,
                    "cancelled" => LeaveStatus.cancelled,
                    "completed" => LeaveStatus.recorded,
                    _ => LeaveStatus.pending // Default fallback
                };
            }
            return LeaveStatus.pending;
        }

        // Convert LeaveStatus enum to Firestore string
        public object ToFirestore(LeaveStatus value)
        {
            return value switch
            {
                LeaveStatus.pending => "Pending",
                LeaveStatus.approvedBySupervisor => "Approved by Supervisor",
                LeaveStatus.approvedByHod => "Approved by HOD",
                LeaveStatus.rejectedBySupervisor => "Rejected by Supervisor",
                LeaveStatus.rejectedByHod => "Rejected by HOD",
                LeaveStatus.cancelled => "Cancelled",
                LeaveStatus.recorded => "Recorded",
                _ => "Pending"
            };
        }
    }
}