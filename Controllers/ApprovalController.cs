using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Leave2Day.Models;
using Leave2Day.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Leave2Day.Controllers
{
    [Authorize] // Requires login for all actions
    public class ApprovalController : Controller
    {
        private readonly IFirebaseService _firebaseService;
        private readonly EmailService _emailService;
        private readonly ILogger<ApprovalController> _logger;

        public ApprovalController(
            IFirebaseService firebaseService,
            EmailService emailService,
            ILogger<ApprovalController> logger)
        {
            _firebaseService = firebaseService;
            _emailService = emailService;
            _logger = logger;
        }

        // GET: Show pending leave requests for approval
        [Authorize(Roles = "Supervisor,HOD")] // Only supervisors and HODs can access
        public async Task<IActionResult> Index()
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var userRole = User.FindFirstValue("Role");

                List<LeaveApplication> pendingRequests;

                // Get requests based on user role
                switch (userRole)
                {
                    case "Supervisor":
                        pendingRequests = await _firebaseService.GetPendingLeaveRequestsForSupervisorAsync(userId);
                        break;
                    case "HOD":
                        pendingRequests = await _firebaseService.GetPendingLeaveRequestsForHodAsync(userId);
                        break;
                    default:
                        return Forbid(); // Access denied for other roles
                }

                ViewBag.UserRole = userRole;
                return View(pendingRequests);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading approvals for user.");
                ModelState.AddModelError("", $"Error loading approvals: {ex.Message}");
                return View(new List<LeaveApplication>());
            }
        }

        // GET: Display leave request details for review
        [HttpGet]
        [Authorize(Roles = "Supervisor,HOD")]
        public async Task<IActionResult> Review(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id))
                    return NotFound();

                var leaveRequest = await _firebaseService.GetLeaveRequestByIdAsync(id);

                if (leaveRequest == null)
                    return NotFound();

                var viewModel = new ApprovalViewModel
                {
                    LeaveRequest = leaveRequest
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading request {RequestId}", id);
                ModelState.AddModelError("", $"Error loading request: {ex.Message}");
                return NotFound();
            }
        }

        // POST: Submit approval/rejection decision
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Supervisor,HOD")]
        public async Task<IActionResult> Review(string id, ApprovalViewModel model)
        {
            try
            {
                var leaveRequest = await _firebaseService.GetLeaveRequestByIdAsync(id);
                if (leaveRequest == null)
                    return NotFound();

                var userRole = User.FindFirstValue("Role");
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (model.IsApproved)
                {
                    // Handle approval based on role
                    switch (userRole)
                    {
                        case "Supervisor":
                            leaveRequest.status = LeaveStatus.approvedBySupervisor;
                            leaveRequest.supervisorActionDate = DateTime.UtcNow;
                            leaveRequest.supervisorFeedback = model.Comments;
                            await _firebaseService.UpdateLeaveRequestAsync(leaveRequest);

                            // Notify HOD that the application is awaiting HOD approval
                            await NotifyHodForApprovalAsync(leaveRequest);
                            break;

                        case "HOD":
                            leaveRequest.status = LeaveStatus.approvedByHod;
                            leaveRequest.hodActionDate = DateTime.UtcNow;
                            leaveRequest.hodFeedback = model.Comments;

                            // HOD is final approver - update leave balance
                            await _firebaseService.UpdateUserLeaveBalanceAsync(
                                leaveRequest.employeeId,
                                leaveRequest.type,
                                leaveRequest.numberOfDays);

                            await _firebaseService.UpdateLeaveRequestAsync(leaveRequest);

                            // Notify HR that application is ready to be recorded
                            await NotifyHrForRecordingAsync(leaveRequest);

                            // Optionally notify applicant of final approval
                            await NotifyApplicantOfFinalDecisionAsync(leaveRequest, approved: true);
                            break;
                    }
                }
                else
                {
                    // Handle rejection based on role
                    switch (userRole)
                    {
                        case "Supervisor":
                            leaveRequest.status = LeaveStatus.rejectedBySupervisor;
                            leaveRequest.supervisorFeedback = model.Comments;
                            break;
                        case "HOD":
                            leaveRequest.status = LeaveStatus.rejectedByHod;
                            leaveRequest.hodFeedback = model.Comments;
                            break;
                    }

                    // update DB and notify applicant of rejection
                    await _firebaseService.UpdateLeaveRequestAsync(leaveRequest);
                    await NotifyApplicantOfFinalDecisionAsync(leaveRequest, approved: false);
                }

                TempData["SuccessMessage"] = $"Leave request has been {(model.IsApproved ? "approved" : "rejected")}.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process request {RequestId}", id);
                ModelState.AddModelError("", $"Failed to process request: {ex.Message}");
                model.LeaveRequest = await _firebaseService.GetLeaveRequestByIdAsync(id);
                return View(model);
            }
        }

        //
        // Helper: find HOD (from employee user record) and send notification
        //
        private async Task NotifyHodForApprovalAsync(LeaveApplication leaveRequest)
        {
            try
            {
                // Load employee user record to find HOD id/email
                var employeeUser = leaveRequest.User ?? await _firebaseService.GetUserByIdAsync(leaveRequest.employeeId);

                string hodEmail = null;
                string hodName = null;

                if (employeeUser != null)
                {
                    // Try HodId -> fetch user
                    var hodId = employeeUser.GetType().GetProperty("HodId")?.GetValue(employeeUser) as string
                                ?? employeeUser.GetType().GetProperty("hodId")?.GetValue(employeeUser) as string;

                    if (!string.IsNullOrEmpty(hodId))
                    {
                        var hodUser = await _firebaseService.GetUserByIdAsync(hodId);
                        if (hodUser != null)
                        {
                            hodEmail = hodUser.email;
                            hodName = hodUser.FullName;
                        }
                    }

                    // Fallback: direct HodEmail / HodName
                    if (string.IsNullOrEmpty(hodEmail))
                    {
                        hodEmail = employeeUser.GetType().GetProperty("HodEmail")?.GetValue(employeeUser) as string
                                   ?? employeeUser.GetType().GetProperty("hod_email")?.GetValue(employeeUser) as string;
                        hodName = employeeUser.GetType().GetProperty("HodName")?.GetValue(employeeUser) as string
                                   ?? employeeUser.GetType().GetProperty("hod_name")?.GetValue(employeeUser) as string
                                   ?? hodName;
                    }
                }

                if (!string.IsNullOrEmpty(hodEmail))
                {
                    await _emailService.SendHodAwaitingApprovalAsync(hodEmail, hodName ?? "HOD", leaveRequest.id);
                    _logger.LogInformation("Sent HOD awaiting-approval email to {HodEmail} for leave {LeaveId}", hodEmail, leaveRequest.id);
                }
                else
                {
                    _logger.LogWarning("Could not find HOD contact for leave {LeaveId}; HOD notification skipped.", leaveRequest.id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to notify HOD for leave {LeaveId}", leaveRequest.id);
            }
        }

        //
        // Helper: find HR contact and notify HR to record the application
        //
        private async Task NotifyHrForRecordingAsync(LeaveApplication leaveRequest)
        {
            try
            {
                var employeeUser = leaveRequest.User ?? await _firebaseService.GetUserByIdAsync(leaveRequest.employeeId);

                string hrEmail = null;
                string hrName = "HR";

                if (employeeUser != null)
                {
                    // Try HrId -> fetch user record
                    var hrId = employeeUser.GetType().GetProperty("HrId")?.GetValue(employeeUser) as string
                               ?? employeeUser.GetType().GetProperty("hrId")?.GetValue(employeeUser) as string;

                    if (!string.IsNullOrEmpty(hrId))
                    {
                        var hrUser = await _firebaseService.GetUserByIdAsync(hrId);
                        if (hrUser != null)
                        {
                            hrEmail = hrUser.email;
                            hrName = hrUser.FullName ?? hrName;
                        }
                    }

                    // Fallback: direct HrEmail property on user
                    if (string.IsNullOrEmpty(hrEmail))
                    {
                        hrEmail = employeeUser.GetType().GetProperty("HrEmail")?.GetValue(employeeUser) as string
                                  ?? employeeUser.GetType().GetProperty("hr_email")?.GetValue(employeeUser) as string;
                    }
                }

                if (!string.IsNullOrEmpty(hrEmail))
                {
                    await _emailService.SendHrRecordAsync(hrEmail, hrName, leaveRequest.id);
                    _logger.LogInformation("Sent HR recording email to {HrEmail} for leave {LeaveId}", hrEmail, leaveRequest.id);
                }
                else
                {
                    _logger.LogWarning("HR contact not found for leave {LeaveId}; HR notification skipped.", leaveRequest.id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to notify HR for leave {LeaveId}", leaveRequest.id);
            }
        }

        //
        // Helper: notify applicant of final decision (approved/rejected)
        //
        private async Task NotifyApplicantOfFinalDecisionAsync(LeaveApplication leaveRequest, bool approved)
        {
            try
            {
                var employeeUser = leaveRequest.User ?? await _firebaseService.GetUserByIdAsync(leaveRequest.employeeId);
                var applicantEmail = employeeUser?.email;
                var applicantName = employeeUser?.FullName ?? leaveRequest.employeeName ?? "Applicant";

                if (!string.IsNullOrEmpty(applicantEmail))
                {
                    // Reuse confirmation template for approvals; swap to a rejection template if you add one
                    await _emailService.SendApplicantConfirmationAsync(applicantEmail, applicantName, leaveRequest.id);
                    _logger.LogInformation("Notified applicant {Email} about final decision for leave {LeaveId}", applicantEmail, leaveRequest.id);
                }
                else
                {
                    _logger.LogWarning("Applicant email missing for leave {LeaveId}; applicant notification skipped.", leaveRequest.id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to notify applicant for leave {LeaveId}", leaveRequest.id);
            }
        }
    }
}
