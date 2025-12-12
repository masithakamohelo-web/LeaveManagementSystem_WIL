using Google.Cloud.Firestore;
using Leave2Day.Converters;
using System.ComponentModel.DataAnnotations;

namespace Leave2Day.Models
{
    [FirestoreData]
    public class LeaveApplication
    {
        [FirestoreDocumentId]
        public string id { get; set; }

        [FirestoreProperty]
        [Required]
        public string employeeId { get; set; }

        [FirestoreProperty]
        public string employeeName { get; set; }

        public User User { get; set; } // Related user data

        [FirestoreProperty(ConverterType = typeof(LeaveTypeConverter))]
        [Required]
        public LeaveType type { get; set; } // Type of leave

        [FirestoreProperty]
        [Required]
        [DataType(DataType.Date)]
        public DateTime startDate { get; set; } // Leave start date

        [FirestoreProperty]
        [Required]
        [DataType(DataType.Date)]
        public DateTime endDate { get; set; } // Leave end date

        public string DocumentPath { get; set; } // Document storage path

        [FirestoreProperty]
        [Required]
        public string reason { get; set; } // Leave reason

        [FirestoreProperty("ProofDocumentLink")]
        public string? ProofDocumentLink { get; set; } // Supporting document link

        [FirestoreProperty(ConverterType = typeof(LeaveStatusConverter))]
        public LeaveStatus status { get; set; } = LeaveStatus.pending; // Current status

        [FirestoreProperty]
        public DateTime appliedDate { get; set; } = DateTime.Now; // Application date

        [FirestoreProperty]
        public DateTime? supervisorActionDate { get; set; } // Supervisor decision date

        [FirestoreProperty]
        public DateTime? hodActionDate { get; set; } // HOD decision date

        [FirestoreProperty]
        [DataType(DataType.Date)]
        public DateTime? CapturedAt { get; set; } // HR processing date

        public string CapturedBy { get; set; } // Processed by HR user

        [FirestoreProperty]
        public string supervisorFeedback { get; set; } // Supervisor comments

        [FirestoreProperty]
        public string hodFeedback { get; set; } // HOD comments

        [FirestoreProperty]
        public int numberOfDays { get; set; } // Total leave days

        [FirestoreProperty]
        public DateTime? HrProcessedAt { get; set; } // HR final processing date

        public string UserId { get; internal set; } // Internal user reference
    }

    // Leave type enumeration
    public enum LeaveType
    {
        Annual,
        Sick,
        Maternity,
        Paternity,
        Emergency
    }

    // Leave status enumeration
    public enum LeaveStatus
    {
        pending,
        approvedBySupervisor,
        rejectedBySupervisor,
        approvedByHod,
        rejectedByHod,
        recorded,
        cancelled
    }
}