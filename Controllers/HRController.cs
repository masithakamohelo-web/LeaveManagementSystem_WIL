using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Leave2Day.Models;
using Leave2Day.Services;
using Google.Cloud.Firestore;

namespace Leave2Day.Controllers
{
    [Authorize(Roles = "HR")]
    public class HRController : Controller
    {
        private readonly IFirebaseService _firebaseService;

        public HRController(IFirebaseService firebaseService)
        {
            _firebaseService = firebaseService;
        }

        // HR Dashboard - View all approved leaves for capturing
        public async Task<IActionResult> Index()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== HRController.Index ===");

                var allRequests = await _firebaseService.GetAllLeaveRequestsAsync();
                var approvedRequests = allRequests.Where(r => r.status == LeaveStatus.approvedByHod).ToList();
                var completedRequests = allRequests.Where(r => r.status == LeaveStatus.recorded).ToList();

                ViewBag.ApprovedRequests = approvedRequests;
                ViewBag.CompletedRequests = completedRequests;
                ViewBag.AllRequests = allRequests;

                System.Diagnostics.Debug.WriteLine($"Approved: {approvedRequests.Count}, Completed: {completedRequests.Count}");

                return View();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in HR Index: {ex}");
                ModelState.AddModelError("", $"Error loading HR dashboard: {ex.Message}");
                return View();
            }
        }

        // Capture Leave - Mark leave as captured and record in system
        [HttpPost]
        public async Task<IActionResult> CaptureLeave(string id)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== Capturing Leave: {id} ===");

                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                await _firebaseService.CaptureLeaveAsync(id, userId);

                TempData["SuccessMessage"] = "Leave captured successfully!";
                TempData["SuccessTitle"] = "Capture Complete";

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR capturing leave: {ex}");
                TempData["ErrorMessage"] = $"Failed to capture leave: {ex.Message}";
                return RedirectToAction("Index");
            }
        }

        // Reports - Generate leave reports
        public async Task<IActionResult> Reports()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== HR Reports ===");

                var allRequests = await _firebaseService.GetAllLeaveRequestsAsync();

                // Department Statistics
                var departmentStats = allRequests
                    .Where(r => r.User != null)
                    .GroupBy(r => r.User.department)
                    .Select(g => new
                    {
                        Department = g.Key,
                        TotalRequests = g.Count(),
                        Pending = g.Count(r => r.status == LeaveStatus.pending),
                        Approved = g.Count(r => r.status == LeaveStatus.approvedByHod || r.status == LeaveStatus.recorded),
                        Rejected = g.Count(r => r.status == LeaveStatus.rejectedByHod),
                        TotalDays = g.Sum(r => r.numberOfDays)
                    })
                    .ToList();

                // Leave Type Statistics
                var leaveTypeStats = allRequests
                    .GroupBy(r => r.type)
                    .Select(g => new
                    {
                        LeaveType = g.Key.ToString(),
                        Count = g.Count(),
                        TotalDays = g.Sum(r => r.numberOfDays),
                        Approved = g.Count(r => r.status == LeaveStatus.approvedByHod || r.status == LeaveStatus.recorded)
                    })
                    .ToList();

                // Monthly Statistics
                var monthlyStats = allRequests
                    .GroupBy(r => new { r.appliedDate.Year, r.appliedDate.Month })
                    .Select(g => new
                    {
                        Month = $"{g.Key.Year}-{g.Key.Month:D2}",
                        Count = g.Count(),
                        TotalDays = g.Sum(r => r.numberOfDays)
                    })
                    .OrderBy(m => m.Month)
                    .ToList();

                ViewBag.DepartmentStats = departmentStats;
                ViewBag.LeaveTypeStats = leaveTypeStats;
                ViewBag.MonthlyStats = monthlyStats;
                ViewBag.TotalRequests = allRequests.Count;
                ViewBag.TotalApproved = allRequests.Count(r => r.status == LeaveStatus.approvedByHod || r.status == LeaveStatus.recorded);
                ViewBag.TotalRejected = allRequests.Count(r => r.status == LeaveStatus.rejectedBySupervisor);
                ViewBag.TotalPending = allRequests.Count(r => r.status == LeaveStatus.recorded || r.status == LeaveStatus.approvedBySupervisor);

                return View();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in Reports: {ex}");
                ModelState.AddModelError("", $"Error generating reports: {ex.Message}");
                return View();
            }
        }

        // Department Report
        public async Task<IActionResult> DepartmentReport(string department)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== Department Report: {department} ===");

                var departmentRequests = await _firebaseService.GetLeaveRequestsByDepartmentAsync(department);

                ViewBag.Department = department;
                ViewBag.TotalRequests = departmentRequests.Count;
                ViewBag.TotalDays = departmentRequests.Sum(r => r.numberOfDays);
                ViewBag.ApprovedCount = departmentRequests.Count(r => r.status == LeaveStatus.approvedByHod || r.status == LeaveStatus.recorded);
                ViewBag.RejectedCount = departmentRequests.Count(r => r.status == LeaveStatus.rejectedBySupervisor);
                ViewBag.PendingCount = departmentRequests.Count(r => r.status == LeaveStatus.pending || r.status == LeaveStatus.approvedByHod);

                return View(departmentRequests);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in Department Report: {ex}");
                ModelState.AddModelError("", $"Error loading department report: {ex.Message}");
                return View(new List<LeaveApplication>());
            }
        }

        // Employee Leave History
        public async Task<IActionResult> EmployeeHistory(string userId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== Employee History: {userId} ===");

                var employee = await _firebaseService.GetUserByIdAsync(userId);
                var employeeRequests = await _firebaseService.GetLeaveRequestsByUserIdAsync(userId);

                ViewBag.Employee = employee;

                return View(employeeRequests);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in Employee History: {ex}");
                ModelState.AddModelError("", $"Error loading employee history: {ex.Message}");
                return View(new List<LeaveApplication>());
            }
        }

        // All Employees - for searching and viewing
        public async Task<IActionResult> Employees()
        {
            try
            {
                var users = await _firebaseService.GetUsersAsync();
                var employees = users.Where(u => u.role == UserRole.Employee || u.role == UserRole.Supervisor || u.role == UserRole.HOD).ToList();

                return View(employees);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in Employees: {ex}");
                ModelState.AddModelError("", $"Error loading employees: {ex.Message}");
                return View(new List<User>());
            }
        }
    }
}