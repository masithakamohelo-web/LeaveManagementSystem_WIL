using Leave2Day.Models;
using Leave2Day.Services;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Google.Cloud.Firestore;

namespace Leave2Day.Controllers
{
    [Authorize] // Ensures only logged-in users can access this controller
    public class DashboardController : Controller
    {
        private readonly IFirebaseService _firebaseService;

        // Constructor injects Firebase service
        public DashboardController(IFirebaseService firebaseService)
        {
            _firebaseService = firebaseService;
        }

        // Determine user role based on ID prefix
        private string DetermineRoleFromId(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return "Employee";

            userId = userId.ToLower();

            if (userId.StartsWith("sup"))
                return "Supervisor";
            if (userId.StartsWith("hod"))
                return "HOD";
            if (userId.StartsWith("hr"))
                return "HR";
            if (userId.StartsWith("emp"))
                return "Employee";

            return "Employee";
        }

        // Dashboard main view - loads data depending on user role
        public async Task<IActionResult> Index()
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var userRole = DetermineRoleFromId(userId);

                System.Diagnostics.Debug.WriteLine($"Dashboard Load - UserId: {userId}, Role: {userRole}");

                // Redirect if no user session found
                if (string.IsNullOrEmpty(userId))
                {
                    return RedirectToAction("Login", "Account");
                }

                ViewBag.UserRole = userRole;

                // Get user details from Firestore
                var user = await _firebaseService.GetUserByIdAsync(userId);

                if (user == null)
                {
                    System.Diagnostics.Debug.WriteLine($"User not found: {userId}");
                    return RedirectToAction("Login", "Account");
                }

                // Check Firestore consistency (debug tool)
                await _firebaseService.CheckFirestoreDataAsync(userId);

                // Fetch recent leave requests for this user
                var leaveRequests = await _firebaseService.GetLeaveRequestsByUserIdAsync(userId);
                ViewBag.User = user;
                ViewBag.RecentLeaveRequests = leaveRequests?.Take(5).ToList() ?? new List<LeaveApplication>();

                // Supervisor dashboard: pending approvals
                if (userRole == "Supervisor")
                {
                    var pendingApprovals = await _firebaseService.GetPendingLeaveRequestsForSupervisorAsync(userId);
                    ViewBag.PendingApprovals = pendingApprovals;
                }
                // HOD dashboard: pending approvals
                else if (userRole == "HOD")
                {
                    var pendingApprovals = await _firebaseService.GetPendingLeaveRequestsForHodAsync(userId);
                    ViewBag.PendingApprovals = pendingApprovals;
                }
                // HR dashboard: view leaves ready for capture
                else if (userRole == "HR")
                {
                    var allRequests = await _firebaseService.GetAllLeaveRequestsAsync();
                    var approvedRequests = allRequests.Where(r => r.status == LeaveStatus.approvedByHod).ToList();
                    ViewBag.ApprovedForCapture = approvedRequests;
                }

                return View();
            }
            catch (Exception ex)
            {
                // Handle errors gracefully
                System.Diagnostics.Debug.WriteLine($"Error in Dashboard Index: {ex}");
                ModelState.AddModelError("", $"Error loading dashboard: {ex.Message}");
                return View();
            }
        }

        // API endpoint: returns user's remaining leave balances
        [HttpGet]
        public async Task<IActionResult> GetUserBalances()
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var user = await _firebaseService.GetUserByIdAsync(userId);

                if (user == null)
                {
                    return Json(new { success = false, message = "User not found" });
                }

                // Debug output for leave balances
                System.Diagnostics.Debug.WriteLine($"=== USER BALANCE DEBUG ===");
                System.Diagnostics.Debug.WriteLine($"Annual: {user.LeaveBalance.Annual} - Used: {user.LeaveBalance.UsedAnnual} = Remaining: {user.RemainingAnnualLeave}");
                System.Diagnostics.Debug.WriteLine($"Sick: {user.LeaveBalance.Sick} - Used: {user.LeaveBalance.UsedSick} = Remaining: {user.RemainingSickLeave}");
                System.Diagnostics.Debug.WriteLine($"Emergency: {user.LeaveBalance.Emergency} - Used: {user.LeaveBalance.UsedEmergency} = Remaining: {user.RemainingEmergencyLeave}");

                // Return balance info as JSON
                var balances = new
                {
                    annual = user.RemainingAnnualLeave,
                    sick = user.RemainingSickLeave,
                    maternity = user.RemainingMaternityLeave,
                    paternity = user.RemainingPaternityLeave,
                    emergency = user.RemainingEmergencyLeave
                };

                return Json(new { success = true, balances });
            }
            catch (Exception ex)
            {
                // Return error as JSON response
                System.Diagnostics.Debug.WriteLine($"Error getting user balances: {ex.Message}");
                return Json(new { success = false, message = "Error retrieving balances" });
            }
        }
    }
}
