using Google.Cloud.Firestore;
using System.ComponentModel.DataAnnotations;

namespace Leave2Day.Models
{
    public class LeaveRequestViewModel
    {
        [Required]
        public LeaveType LeaveType { get; set; } // Type of leave being requested

        [Required]
        [DataType(DataType.Date)]
        public DateTime StartDate { get; set; } // Leave start date

        [Required]
        [DataType(DataType.Date)]
        public DateTime EndDate { get; set; } // Leave end date

        [Required]
        public string Reason { get; set; } // Reason for leave

        [FirestoreProperty]
        public string? ProofDocumentLink { get; set; } // Optional supporting document
    }
}