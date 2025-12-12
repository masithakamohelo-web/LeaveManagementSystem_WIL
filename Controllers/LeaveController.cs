using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Leave2Day.Models;
using Leave2Day.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Leave2Day.Controllers
{
    [Authorize]
    public class LeaveController : Controller
    {
        private readonly IFirebaseService _firebaseService;
        private readonly EmailService _emailService;
        private readonly ILogger<LeaveController> _logger;

        public LeaveController(
            IFirebaseService firebaseService,
            EmailService emailService,
            ILogger<LeaveController> logger)
        {
            _firebaseService = firebaseService;
            _emailService = emailService;
            _logger = logger;
        }

        // Helper to safely read possible string properties from dynamic User objects
        private string GetStringProp(object obj, string propName)
        {
            if (obj == null) return null;
            var p = obj.GetType().GetProperty(propName);
            if (p == null) return null;
            return p.GetValue(obj) as string;
        }

        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var leaveRequests = await _firebaseService.GetLeaveRequestsByUserIdAsync(userId);
            return View(leaveRequests);
        }

        [HttpGet]
        public async Task<IActionResult> Apply()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _firebaseService.GetUserByIdAsync(userId);
            if (user == null)
            {
                TempData["Error"] = "User not found. Please log in again.";
                return RedirectToAction("Index", "Dashboard");
            }
            ViewBag.User = user;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Apply(LeaveRequestViewModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!ModelState.IsValid)
            {
                ViewBag.User = await _firebaseService.GetUserByIdAsync(userId);
                return View(model);
            }

            if (model.EndDate < model.StartDate)
            {
                ModelState.AddModelError("", "End Date cannot be before Start Date.");
                ViewBag.User = await _firebaseService.GetUserByIdAsync(userId);
                return View(model);
            }

            var user = await _firebaseService.GetUserByIdAsync(userId);
            if (user == null)
            {
                ModelState.AddModelError("", "User not found.");
                return View(model);
            }

            var totalDays = (model.EndDate - model.StartDate).Days + 1;
            bool hasSufficientBalance = model.LeaveType switch
            {
                LeaveType.Annual => totalDays <= user.RemainingAnnualLeave,
                LeaveType.Sick => totalDays <= user.RemainingSickLeave,
                LeaveType.Maternity => totalDays <= user.RemainingMaternityLeave,
                LeaveType.Paternity => totalDays <= user.RemainingPaternityLeave,
                LeaveType.Emergency => totalDays <= user.RemainingEmergencyLeave,
                _ => false
            };

            if (!hasSufficientBalance)
            {
                var availableDays = model.LeaveType switch
                {
                    LeaveType.Annual => user.RemainingAnnualLeave,
                    LeaveType.Sick => user.RemainingSickLeave,
                    LeaveType.Maternity => user.RemainingMaternityLeave,
                    LeaveType.Paternity => user.RemainingPaternityLeave,
                    LeaveType.Emergency => user.RemainingEmergencyLeave,
                    _ => 0
                };

                ModelState.AddModelError("", $"Insufficient {model.LeaveType} leave balance. Available: {availableDays}, Requested: {totalDays}");
                ViewBag.User = user;
                return View(model);
            }

            var leave = new LeaveApplication
            {
                employeeId = user.id,
                employeeName = user.FullName,
                User = user,
                type = model.LeaveType,
                startDate = model.StartDate.ToUniversalTime(),
                endDate = model.EndDate.ToUniversalTime(),
                reason = model.Reason,
                ProofDocumentLink = model.ProofDocumentLink?.Trim(),
                status = LeaveStatus.pending,
                numberOfDays = totalDays,
                appliedDate = DateTime.UtcNow
            };

            try
            {
                // Save leave request and get generated Firestore ID
                string leaveId = await _firebaseService.CreateLeaveRequestAsync(leave);
                leave.id = leaveId;

                // Send applicant confirmation email — non-blocking (log on failure)
                try
                {
                    var applicantEmail = user.email;
                    var applicantName = user.FullName ?? user.email ?? "Employee";

                    if (!string.IsNullOrEmpty(applicantEmail))
                    {
                        await _emailService.SendApplicantConfirmationAsync(applicantEmail, applicantName, leaveId);
                        _logger.LogInformation("Applicant email sent to {Email} for leave {LeaveId}", applicantEmail, leaveId);
                    }
                    else
                    {
                        _logger.LogWarning("Applicant email missing for user {UserId}", user.id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send applicant confirmation email for leave {LeaveId}", leaveId);
                    // don't fail the request — DB save succeeded
                }

                // Send notification to supervisor (try to resolve supervisor record)
                try
                {
                    // Prefer a helper method on IFirebaseService, fallback to properties on user object
                    User supUser = null;
                    try
                    {
                        supUser = await _firebaseService.GetSupervisorForEmployeeAsync(user.id);
                    }
                    catch
                    {
                        // ignore - attempt fallback
                    }

                    string supervisorEmail = supUser?.email ?? GetStringProp(user, "SupervisorEmail") ?? GetStringProp(user, "supervisor_email");
                    string supervisorName = supUser?.FullName ?? GetStringProp(user, "SupervisorName") ?? GetStringProp(user, "supervisor_name") ?? "Supervisor";

                    if (!string.IsNullOrEmpty(supervisorEmail))
                    {
                        await _emailService.SendSupervisorAwaitingApprovalAsync(supervisorEmail, supervisorName, leaveId);
                        _logger.LogInformation("Supervisor email sent to {Email} for leave {LeaveId}", supervisorEmail, leaveId);
                    }
                    else
                    {
                        _logger.LogWarning("Supervisor email not found for employee {UserId}", user.id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send supervisor email for leave {LeaveId}", leaveId);
                }

                TempData["Success"] = "Leave request submitted successfully!";
                return RedirectToAction("Index", "Dashboard");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to submit leave request for user {UserId}", user.id);
                ModelState.AddModelError("", $"Failed to submit leave request: {ex.Message}");
                ViewBag.User = user;
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> History()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var requests = await _firebaseService.GetLeaveRequestsByUserIdAsync(userId);
            return View(requests);
        }

        public async Task<IActionResult> Details(string id)
        {
            var leaveRequest = await _firebaseService.GetLeaveRequestByIdAsync(id);
            if (leaveRequest == null) return NotFound();
            return View(leaveRequest);
        }

        public async Task<IActionResult> AllLeaveRequests()
        {
            try
            {
                var allLeaveRequests = await _firebaseService.GetAllLeaveRequestsAsync();
                return View(allLeaveRequests);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load all leave requests.");
                return View("Error");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                TempData["LeaveCancelError"] = "Invalid leave request.";
                return RedirectToAction("Index", "Dashboard");
            }

            var leave = await _firebaseService.GetLeaveRequestByIdAsync(id);
            if (leave == null)
            {
                TempData["LeaveCancelError"] = "Leave request not found.";
                return RedirectToAction("Index", "Dashboard");
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (leave.employeeId != userId)
            {
                TempData["LeaveCancelError"] = "You can only cancel your own leave.";
                return RedirectToAction("Index", "Dashboard");
            }

            if (leave.status != LeaveStatus.pending)
            {
                TempData["LeaveCancelError"] = "You can only cancel leave requests that are still pending.";
                return RedirectToAction("Index", "Dashboard");
            }

            leave.status = LeaveStatus.cancelled;
            await _firebaseService.UpdateLeaveRequestAsync(leave);

            TempData["LeaveCancelSuccess"] = "Leave request cancelled successfully.";
            return RedirectToAction("Index", "Dashboard");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SupervisorAction(string applicationId, string action, string feedback = null)
        {
            if (string.IsNullOrEmpty(applicationId))
                return BadRequest();

            var app = await _firebaseService.GetLeaveRequestByIdAsync(applicationId);
            if (app == null)
                return NotFound();

            // TODO: Enforce current user is supervisor for this leave application

            if (string.Equals(action, "approve", StringComparison.OrdinalIgnoreCase))
            {
                app.status = LeaveStatus.approvedBySupervisor;
                app.supervisorActionDate = DateTime.UtcNow;
                app.supervisorFeedback = feedback;

                await _firebaseService.UpdateLeaveRequestAsync(app);

                try
                {
                    string hodEmail = null;
                    string hodName = null;

                    try
                    {
                        var hodId = (app.User?.GetType().GetProperty("HodId")?.GetValue(app.User) as string)
                                 ?? (app.User?.GetType().GetProperty("hodId")?.GetValue(app.User) as string);

                        if (!string.IsNullOrEmpty(hodId))
                        {
                            var hodUser = await _firebaseService.GetUserByIdAsync(hodId);
                            if (hodUser != null)
                            {
                                hodEmail = hodUser.email;
                                hodName = hodUser.FullName;
                            }
                        }
                    }
                    catch { }

                    if (string.IsNullOrEmpty(hodEmail))
                    {
                        hodEmail = app.User?.GetType().GetProperty("HodEmail")?.GetValue(app.User) as string
                                ?? app.User?.GetType().GetProperty("hod_email")?.GetValue(app.User) as string;
                        hodName = app.User?.GetType().GetProperty("HodName")?.GetValue(app.User) as string
                                ?? app.User?.GetType().GetProperty("hod_name")?.GetValue(app.User) as string
                                ?? hodName;
                    }

                    if (!string.IsNullOrEmpty(hodEmail))
                    {
                        await _emailService.SendHodAwaitingApprovalAsync(hodEmail, hodName ?? "HOD", app.id);
                        _logger.LogInformation("HOD notification sent to {HodEmail} for leave {LeaveId}", hodEmail, app.id);
                    }
                    else
                    {
                        _logger.LogWarning("HOD contact not found for leave {LeaveId}.", app.id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send HOD notification for leave {LeaveId}", app.id);
                }
            }
            else if (string.Equals(action, "reject", StringComparison.OrdinalIgnoreCase))
            {
                app.status = LeaveStatus.rejectedBySupervisor;
                app.supervisorActionDate = DateTime.UtcNow;
                app.supervisorFeedback = feedback;

                await _firebaseService.UpdateLeaveRequestAsync(app);

                try
                {
                    var applicantEmail = app.User?.email ?? app.employeeName;
                    if (!string.IsNullOrEmpty(applicantEmail))
                    {
                        await _emailService.SendApplicantConfirmationAsync(applicantEmail, app.employeeName, app.id);
                        _logger.LogInformation("Applicant rejection email sent to {Email} for leave {LeaveId}", applicantEmail, app.id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send applicant rejection email for leave {LeaveId}", app.id);
                }
            }

            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> HodAction(string applicationId, string action, string feedback = null)
        {
            if (string.IsNullOrEmpty(applicationId))
                return BadRequest();

            var app = await _firebaseService.GetLeaveRequestByIdAsync(applicationId);
            if (app == null)
                return NotFound();

            // TODO: Enforce current user is HOD for this leave application

            if (string.Equals(action, "approve", StringComparison.OrdinalIgnoreCase))
            {
                app.status = LeaveStatus.approvedByHod;
                app.hodActionDate = DateTime.UtcNow;
                app.hodFeedback = feedback;

                await _firebaseService.UpdateLeaveRequestAsync(app);

                try
                {
                    string hrEmail = null;
                    string hrName = "HR";

                    hrEmail = app.User?.GetType().GetProperty("HrEmail")?.GetValue(app.User) as string
                           ?? app.User?.GetType().GetProperty("hr_email")?.GetValue(app.User) as string;

                    if (!string.IsNullOrEmpty(hrEmail))
                    {
                        await _emailService.SendHrRecordAsync(hrEmail, hrName, app.id);
                        _logger.LogInformation("HR notification sent to {HrEmail} for leave {LeaveId}", hrEmail, app.id);
                    }
                    else
                    {
                        _logger.LogWarning("HR contact not found for leave {LeaveId}.", app.id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send HR notification for leave {LeaveId}", app.id);
                }
            }
            else if (string.Equals(action, "reject", StringComparison.OrdinalIgnoreCase))
            {
                app.status = LeaveStatus.rejectedByHod;
                app.hodActionDate = DateTime.UtcNow;
                app.hodFeedback = feedback;

                await _firebaseService.UpdateLeaveRequestAsync(app);

                try
                {
                    var applicantEmail = app.User?.email ?? app.employeeName;
                    if (!string.IsNullOrEmpty(applicantEmail))
                    {
                        await _emailService.SendApplicantConfirmationAsync(applicantEmail, app.employeeName, app.id);
                        _logger.LogInformation("Applicant notified of HOD rejection for leave {LeaveId}", app.id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to notify applicant of HOD rejection for leave {LeaveId}", app.id);
                }
            }

            return Ok();
        }
    }
}
