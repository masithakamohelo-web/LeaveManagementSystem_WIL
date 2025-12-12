namespace Leave2Day.Models
{
    public class ApprovalViewModel
    {
        public LeaveApplication LeaveRequest { get; set; } // Leave request details
        public string Comments { get; set; } // Approver's feedback comments
        public bool IsApproved { get; set; } // Approval decision (true = approved, false = rejected)

        // Get proof document link from leave request
        public string ProofDocumentLink => LeaveRequest?.ProofDocumentLink;
    }
}