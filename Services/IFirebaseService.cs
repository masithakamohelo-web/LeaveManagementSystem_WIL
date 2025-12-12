using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Net.Http.Headers;
using Leave2Day.Models;


namespace Leave2Day.Services
{
    public interface IFirebaseService
    {
        // User-related
        Task<User> AuthenticateUserAsync(string email, string password);
        Task<User> GetUserByIdAsync(string userId);
        Task<User> GetUserByEmailAsync(string email);
        Task<List<User>> GetUsersAsync();
        Task UpdateUserLeaveBalanceAsync(string userId, LeaveType leaveType, int days, bool isCancellation = false);


        // Leave request-related
        Task<string> CreateLeaveRequestAsync(LeaveApplication leaveRequest);
        Task<LeaveApplication> GetLeaveRequestByIdAsync(string id);
        Task<List<LeaveApplication>> GetLeaveRequestsByUserIdAsync(string userId);
        Task<List<LeaveApplication>> GetLeaveRequestsByUserAsync(string userId);
        Task<List<LeaveApplication>> GetLeaveRequestsAsync(string userId, UserRole role);
        Task<List<LeaveApplication>> GetPendingLeaveRequestsForSupervisorAsync(string supervisorId);
        Task<List<LeaveApplication>> GetPendingLeaveRequestsForHodAsync(string hodId);
        Task<List<LeaveApplication>> GetAllLeaveRequestsAsync();
        Task<List<LeaveApplication>> GetApprovedLeaveRequestsAsync();
        Task<List<LeaveApplication>> GetLeaveRequestsByDepartmentAsync(string department);
        Task UpdateLeaveRequestAsync(LeaveApplication leaveRequest);
        Task CaptureLeaveAsync(string leaveRequestId, string hrUserId);
        Task AddLeaveRequestAsync(LeaveApplication leaveRequest);
        Task CheckFirestoreDataAsync(string userId);
        Task<List<LeaveApplication>> GetAllLeaveRequests();


        Task<User> GetUserProfileAsync(string email);
        Task<bool> UpdateUserProfileAsync(string email, string firstName, string lastName, string phoneNumber);


        Task<bool> CancelLeaveAsync(string leaveRequestId);
        Task<Dictionary<string, object>> GetRawDocumentAsync(string collection, string id);

        // --- New helper methods to support email/approval flow ---
        // Returns the supervisor user record (or null)
        Task<User> GetSupervisorForEmployeeAsync(string employeeId);
        // Returns the hod user record (or null)
        Task<User> GetHodForEmployeeAsync(string employeeId);
        // Returns an HR user record (or null) that should record the leave
        Task<User> GetHrForEmployeeAsync(string employeeId);
    }
}