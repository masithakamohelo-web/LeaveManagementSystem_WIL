using Google.Api;
using Google.Cloud.Firestore;
using Leave2Day.Converters;
using Leave2Day.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Leave2Day.Services
{
    public class FirestoreService : IFirebaseService
    {
        private readonly FirestoreDb _firestoreDb;
        private readonly IConfiguration _configuration;


        // Constructor: Initializes Firestore connection using configuration settings
        public FirestoreService(IConfiguration configuration)
        {
            var projectId = configuration["Google:ProjectId"];
            var credentialPath = configuration["Google:CredentialPath"];

            if (string.IsNullOrEmpty(projectId))
                throw new Exception("Firestore project ID not found in configuration.");

            if (string.IsNullOrEmpty(credentialPath))
                throw new Exception("Firestore credential path not found in configuration.");

            // Set environment variable for Google Cloud credentials
            Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", credentialPath);
            _firestoreDb = FirestoreDb.Create(projectId);
            _configuration = configuration;
        }

        // Authenticates user using Firebase Authentication API
        // Authenticates user using Firebase Authentication API
        public async Task<User> AuthenticateUserAsync(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("Email cannot be null or empty", nameof(email));

            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Password cannot be null or empty", nameof(password));

            var apiKey = _configuration["Google:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new Exception("Firebase API key is missing in configuration.");

            using var http = new HttpClient();
            var payload = new Dictionary<string, object>
    {
        { "email", email },
        { "password", password },
        { "returnSecureToken", true }
    };

            HttpResponseMessage response;
            try
            {
                response = await http.PostAsJsonAsync(
                    $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={apiKey}",
                    payload
                );
            }
            catch (Exception ex)
            {
                throw new Exception("Unable to connect to authentication service. Please try again.", ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                var errorMessage = ParseFirebaseAuthError(errorContent);
                throw new Exception(errorMessage);
            }

            FirebaseLoginResponse loginResult;
            try
            {
                loginResult = await response.Content.ReadFromJsonAsync<FirebaseLoginResponse>();
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to process authentication response.", ex);
            }

            if (loginResult == null || string.IsNullOrEmpty(loginResult.LocalId))
                throw new Exception("Authentication service returned an invalid response.");

            // 🔹 Fetch Firestore user by email
            var query = _firestoreDb.Collection("users")
                .WhereEqualTo("email", email)
                .Limit(1);

            var snapshot = await query.GetSnapshotAsync();
            if (!snapshot.Documents.Any())
                throw new Exception("User profile not found in system.");

            var doc = snapshot.Documents.First();
            var user = doc.ConvertTo<User>();
            user.id = doc.Id;

            System.Diagnostics.Debug.WriteLine($"Login successful for {email}, Firestore ID: {user.id}, Role: {user.role}");

            return user;
        }


        // Add this helper method to parse Firebase Auth errors
        private string ParseFirebaseAuthError(string errorContent)
        {
            try
            {
                // Try to parse the JSON error response
                var errorJson = System.Text.Json.JsonDocument.Parse(errorContent);
                var error = errorJson.RootElement.GetProperty("error");

                if (error.TryGetProperty("message", out var messageElement))
                {
                    var message = messageElement.GetString();

                    return message switch
                    {
                        "INVALID_LOGIN_CREDENTIALS" => "Invalid email or password. Please check your credentials and try again.",
                        "EMAIL_NOT_FOUND" => "No account found with this email address.",
                        "INVALID_PASSWORD" => "Invalid password. Please try again.",
                        "USER_DISABLED" => "This account has been disabled. Please contact your administrator.",
                        "TOO_MANY_ATTEMPTS_TRY_LATER" => "Too many failed login attempts. Please try again later.",
                        "OPERATION_NOT_ALLOWED" => "Password sign-in is disabled for this project.",
                        _ => "Authentication failed. Please try again."
                    };
                }
            }
            catch
            {
                // If JSON parsing fails, return a generic error
            }

            return "Invalid email or password. Please check your credentials and try again.";
        }
        // Response model for Firebase Authentication
        public class FirebaseLoginResponse
        {
            public string IdToken { get; set; }
            public string Email { get; set; }
            public string RefreshToken { get; set; }
            public string ExpiresIn { get; set; }
            public string LocalId { get; set; }
        }

        // Retrieves user by ID with detailed leave balance debugging
        public async Task<User> GetUserByIdAsync(string userId)
        {
            try
            {
                var doc = await _firestoreDb.Collection("users").Document(userId).GetSnapshotAsync();
                if (!doc.Exists)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ User not found: {userId}");
                    return null;
                }

                var user = doc.ConvertTo<User>();
                user.id = doc.Id;

                // Debug: Check actual Firestore data for leave balances
                var data = doc.ToDictionary();
                if (data.ContainsKey("leaveBalance") && data["leaveBalance"] is Dictionary<string, object> leaveBalanceData)
                {
                    System.Diagnostics.Debug.WriteLine($"=== FIRESTORE LEAVE BALANCE ===");
                    System.Diagnostics.Debug.WriteLine($"usedSick from Firestore: {leaveBalanceData.GetValueOrDefault("usedSick")}");
                    System.Diagnostics.Debug.WriteLine($"usedAnnual from Firestore: {leaveBalanceData.GetValueOrDefault("usedAnnual")}");
                }

                System.Diagnostics.Debug.WriteLine($"=== USER OBJECT AFTER CONVERSION ===");
                System.Diagnostics.Debug.WriteLine($"User.LeaveBalance.UsedSick: {user.LeaveBalance?.UsedSick}");
                System.Diagnostics.Debug.WriteLine($"User.RemainingSickLeave: {user.RemainingSickLeave}");

                return user;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in GetUserByIdAsync: {ex.Message}");
                return null;
            }
        }

        // Finds user by email address
        public async Task<User> GetUserByEmailAsync(string email)
        {
            var query = _firestoreDb.Collection("users").WhereEqualTo("email", email).Limit(1);
            var snapshot = await query.GetSnapshotAsync();

            if (!snapshot.Documents.Any()) return null;

            var doc = snapshot.Documents.First();
            var user = doc.ConvertTo<User>();
            user.id = doc.Id;
            return user;
        }

        // Retrieves all users from Firestore
        public async Task<List<User>> GetUsersAsync()
        {
            var snapshot = await _firestoreDb.Collection("users").GetSnapshotAsync();
            return snapshot.Documents.Select(d =>
            {
                var u = d.ConvertTo<User>();
                u.id = d.Id;
                return u;
            }).ToList();
        }

        // Updates user's leave balance when leave is approved or cancelled
        public async Task UpdateUserLeaveBalanceAsync(string userId, LeaveType leaveType, int days, bool isCancellation = false)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== UPDATE USER LEAVE BALANCE ===");
                System.Diagnostics.Debug.WriteLine($"UserId: {userId}, Type: {leaveType}, Days: {days}, IsCancellation: {isCancellation}");

                // Determine which leave balance field to update based on leave type
                var fieldName = leaveType switch
                {
                    LeaveType.Annual => "leaveBalance.usedAnnual",
                    LeaveType.Sick => "leaveBalance.usedSick",
                    LeaveType.Maternity => "leaveBalance.usedMaternity",
                    LeaveType.Paternity => "leaveBalance.usedPaternity",
                    LeaveType.Emergency => "leaveBalance.usedEmergency",
                    _ => "leaveBalance.usedAnnual"
                };

                System.Diagnostics.Debug.WriteLine($"Field to update: {fieldName}");

                // Get current user document directly from Firestore
                var docRef = _firestoreDb.Collection("users").Document(userId);
                var snapshot = await docRef.GetSnapshotAsync();

                if (!snapshot.Exists)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ User document not found: {userId}");
                    return;
                }

                // Extract current leave balance value from Firestore
                int currentValue = 0;
                var data = snapshot.ToDictionary();

                if (data.ContainsKey("leaveBalance") && data["leaveBalance"] is Dictionary<string, object> leaveBalance)
                {
                    var usedField = leaveType switch
                    {
                        LeaveType.Annual => "usedAnnual",
                        LeaveType.Sick => "usedSick",
                        LeaveType.Maternity => "usedMaternity",
                        LeaveType.Paternity => "usedPaternity",
                        LeaveType.Emergency => "usedEmergency",
                        _ => "usedAnnual"
                    };

                    if (leaveBalance.ContainsKey(usedField))
                    {
                        currentValue = Convert.ToInt32(leaveBalance[usedField]);
                        System.Diagnostics.Debug.WriteLine($"Current value from Firestore: {currentValue}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Field {usedField} not found in leaveBalance, using 0");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"leaveBalance object not found in user document");
                }

                // Calculate new balance: add days for new leave, subtract for cancellation
                var newValue = isCancellation ? currentValue - days : currentValue + days;
                newValue = Math.Max(0, newValue); // Prevent negative values

                System.Diagnostics.Debug.WriteLine($"New value: {newValue}");

                // Update Firestore with new balance
                var updateData = new Dictionary<string, object>
                {
                    [fieldName] = newValue
                };

                System.Diagnostics.Debug.WriteLine($"Updating Firestore with: {fieldName} = {newValue}");
                await docRef.UpdateAsync(updateData);

                System.Diagnostics.Debug.WriteLine($"✅ Successfully updated balance for user {userId}: {currentValue} -> {newValue}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ ERROR in UpdateUserLeaveBalanceAsync: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        // Creates a new leave request in Firestore
        public async Task<string> CreateLeaveRequestAsync(LeaveApplication leaveRequest)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== CREATING LEAVE REQUEST ===");
                System.Diagnostics.Debug.WriteLine($"Employee: {leaveRequest.employeeId}");
                System.Diagnostics.Debug.WriteLine($"Type: {leaveRequest.type}");
                System.Diagnostics.Debug.WriteLine($"Start: {leaveRequest.startDate}, End: {leaveRequest.endDate}");

                // Set initial leave request properties
                leaveRequest.appliedDate = DateTime.UtcNow;
                leaveRequest.numberOfDays = (leaveRequest.endDate - leaveRequest.startDate).Days + 1;
                leaveRequest.status = LeaveStatus.pending;

                if (string.IsNullOrEmpty(leaveRequest.employeeId))
                    throw new ArgumentException("Employee ID is required");

                // Prepare data for Firestore
                var data = new Dictionary<string, object>
                {
                    {"id", "" },
                    {"employeeId", leaveRequest.employeeId },
                    {"employeeName", leaveRequest.employeeName ?? "" },
                    {"type", leaveRequest.type.ToString() },
                    {"startDate", Timestamp.FromDateTime(leaveRequest.startDate.ToUniversalTime()) },
                    {"endDate", Timestamp.FromDateTime(leaveRequest.endDate.ToUniversalTime()) },
                    {"ProofDocumentLink", leaveRequest.ProofDocumentLink ?? "" },
                    {"reason", leaveRequest.reason ?? "" },
                    {"status", "Pending" },
                    {"numberOfDays", leaveRequest.numberOfDays },
                    {"appliedDate", Timestamp.FromDateTime(leaveRequest.appliedDate.ToUniversalTime()) },
                    {"lastUpdated", Timestamp.GetCurrentTimestamp() },
                    {"supervisorFeedback","" },
                    {"hodFeedback","" }
                };

                System.Diagnostics.Debug.WriteLine("Saving to Firestore...");

                // Create document and update with generated ID
                var docRef = await _firestoreDb.Collection("leaveApplications").AddAsync(data);
                await docRef.UpdateAsync("id", docRef.Id);

                System.Diagnostics.Debug.WriteLine($"✅ Successfully created leave request with ID: {docRef.Id}");

                return docRef.Id;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error creating leave request: {ex}");
                throw;
            }
        }

        // Alias for CreateLeaveRequestAsync
        public async Task AddLeaveRequestAsync(LeaveApplication leaveRequest)
        {
            await CreateLeaveRequestAsync(leaveRequest);
        }

        // Supervisor approves a leave request
        public async Task<bool> ApproveLeaveBySupervisorAsync(string leaveRequestId, string supervisorRemarks, string supervisorId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== SUPERVISOR APPROVING LEAVE ===");
                System.Diagnostics.Debug.WriteLine($"Leave: {leaveRequestId}, Supervisor: {supervisorId}");

                var leave = await GetLeaveRequestByIdAsync(leaveRequestId);
                if (leave == null)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Leave request {leaveRequestId} not found");
                    return false;
                }

                // Verify supervisor has authority over this employee
                var employee = await GetUserByIdAsync(leave.employeeId);
                if (employee?.supervisorId != supervisorId)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Supervisor {supervisorId} not authorized for employee {leave.employeeId}");
                    return false;
                }

                // Update leave status and supervisor details
                var updateData = new Dictionary<string, object>
                {
                    {"status", "Approved by Supervisor" },
                    {"SupervisorRemarks", supervisorRemarks ?? "" },
                    {"SupervisorActionDate", DateTime.UtcNow },
                    {"lastUpdated", Timestamp.GetCurrentTimestamp() }
                };

                await _firestoreDb.Collection("leaveApplications").Document(leaveRequestId).UpdateAsync(updateData);

                System.Diagnostics.Debug.WriteLine($"✅ Supervisor approved: {leaveRequestId}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in ApproveLeaveBySupervisorAsync: {ex}");
                return false;
            }
        }

        // Supervisor rejects a leave request
        public async Task<bool> RejectLeaveBySupervisorAsync(string leaveRequestId, string supervisorRemarks, string supervisorId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== SUPERVISOR REJECTING LEAVE ===");
                System.Diagnostics.Debug.WriteLine($"Leave: {leaveRequestId}, Supervisor: {supervisorId}");

                var leave = await GetLeaveRequestByIdAsync(leaveRequestId);
                if (leave == null)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Leave request {leaveRequestId} not found");
                    return false;
                }

                // Verify supervisor authorization
                var employee = await GetUserByIdAsync(leave.employeeId);
                if (employee?.supervisorId != supervisorId)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Supervisor {supervisorId} not authorized for employee {leave.employeeId}");
                    return false;
                }

                var updateData = new Dictionary<string, object>
                {
                    {"status", "Rejected" },
                    {"SupervisorRemarks", supervisorRemarks ?? "" },
                    {"SupervisorActionDate", DateTime.UtcNow },
                    {"lastUpdated", Timestamp.GetCurrentTimestamp() }
                };

                await _firestoreDb.Collection("leaveApplications").Document(leaveRequestId).UpdateAsync(updateData);

                System.Diagnostics.Debug.WriteLine($"✅ Supervisor rejected: {leaveRequestId}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in RejectLeaveBySupervisorAsync: {ex}");
                return false;
            }
        }

        // HOD approves a leave request (final approval)
        public async Task<bool> ApproveLeaveByHodAsync(string leaveRequestId, string hodRemarks, string hodId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== HOD APPROVING LEAVE ===");
                System.Diagnostics.Debug.WriteLine($"Leave: {leaveRequestId}, HOD: {hodId}");

                var leave = await GetLeaveRequestByIdAsync(leaveRequestId);
                if (leave == null)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Leave request {leaveRequestId} not found");
                    return false;
                }

                // Verify HOD has authority over this employee
                var employee = await GetUserByIdAsync(leave.employeeId);
                if (employee?.hodId != hodId)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ HOD {hodId} not authorized for employee {leave.employeeId}");
                    return false;
                }

                var updateData = new Dictionary<string, object>
                {
                    {"status", "Approved by HOD" },
                    {"HodRemarks", hodRemarks ?? "" },
                    {"HodActionDate", DateTime.UtcNow },
                    {"lastUpdated", Timestamp.GetCurrentTimestamp() }
                };

                // HOD approval deducts leave balance immediately (final approval)
                await UpdateUserLeaveBalanceAsync(leave.employeeId, leave.type, leave.numberOfDays, false);

                await _firestoreDb.Collection("leaveApplications").Document(leaveRequestId).UpdateAsync(updateData);

                System.Diagnostics.Debug.WriteLine($"✅ HOD approved: {leaveRequestId}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in ApproveLeaveByHodAsync: {ex}");
                return false;
            }
        }

        // HOD rejects a leave request
        public async Task<bool> RejectLeaveByHodAsync(string leaveRequestId, string hodRemarks, string hodId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== HOD REJECTING LEAVE ===");
                System.Diagnostics.Debug.WriteLine($"Leave: {leaveRequestId}, HOD: {hodId}");

                var leave = await GetLeaveRequestByIdAsync(leaveRequestId);
                if (leave == null)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Leave request {leaveRequestId} not found");
                    return false;
                }

                // Verify HOD authorization
                var employee = await GetUserByIdAsync(leave.employeeId);
                if (employee?.hodId != hodId)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ HOD {hodId} not authorized for employee {leave.employeeId}");
                    return false;
                }

                var updateData = new Dictionary<string, object>
                {
                    {"status", "Rejected" },
                    {"HodRemarks", hodRemarks ?? "" },
                    {"HodActionDate", DateTime.UtcNow },
                    {"lastUpdated", Timestamp.GetCurrentTimestamp() }
                };

                await _firestoreDb.Collection("leaveApplications").Document(leaveRequestId).UpdateAsync(updateData);

                System.Diagnostics.Debug.WriteLine($"✅ HOD rejected: {leaveRequestId}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in RejectLeaveByHodAsync: {ex}");
                return false;
            }
        }

        // Retrieves a specific leave request by ID
        public async Task<LeaveApplication> GetLeaveRequestByIdAsync(string id)
        {
            try
            {
                var doc = await _firestoreDb.Collection("leaveApplications").Document(id).GetSnapshotAsync();
                if (!doc.Exists) return null;

                // Manual conversion for better control over data mapping
                var data = doc.ToDictionary();
                var leave = MapDictionaryToLeaveApplication(data, doc.Id);

                // Load associated user data
                if (!string.IsNullOrEmpty(leave.employeeId))
                {
                    leave.User = await GetUserByIdAsync(leave.employeeId);
                }

                return leave;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetLeaveRequestByIdAsync: {ex}");
                return null;
            }
        }

        // Gets all leave requests for a specific user
        public async Task<List<LeaveApplication>> GetLeaveRequestsByUserIdAsync(string userId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Getting leave requests for user: {userId}");

                var query = _firestoreDb.Collection("leaveApplications")
                    .WhereEqualTo("employeeId", userId)
                    .OrderByDescending("appliedDate");

                var snapshot = await query.GetSnapshotAsync();
                var requests = new List<LeaveApplication>();

                System.Diagnostics.Debug.WriteLine($"Found {snapshot.Documents.Count} documents in query");

                // Process each leave request document
                foreach (var doc in snapshot.Documents)
                {
                    try
                    {
                        var data = doc.ToDictionary();
                        var leaveApp = MapDictionaryToLeaveApplication(data, doc.Id);

                        // Load user information for the leave request
                        if (!string.IsNullOrEmpty(leaveApp.employeeId))
                        {
                            leaveApp.User = await GetUserByIdAsync(leaveApp.employeeId);
                        }

                        requests.Add(leaveApp);
                        System.Diagnostics.Debug.WriteLine($"Successfully added leave {doc.Id} with status: {leaveApp.status}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error processing document {doc.Id}: {ex}");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"Returning {requests.Count} leave requests");
                return requests;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetLeaveRequestsByUserIdAsync: {ex}");
                return new List<LeaveApplication>();
            }
        }

        // Alias for GetLeaveRequestsByUserIdAsync
        public async Task<List<LeaveApplication>> GetLeaveRequestsByUserAsync(string userId)
            => await GetLeaveRequestsByUserIdAsync(userId);

        // Gets leave requests filtered by user role
        public async Task<List<LeaveApplication>> GetLeaveRequestsAsync(string userId, UserRole role)
        {
            var allRequests = await GetAllLeaveRequestsAsync();
            var allUsers = await GetUsersAsync();

            return role switch
            {
                UserRole.Employee => allRequests.Where(r => r.employeeId == userId).ToList(),
                UserRole.Supervisor => allRequests
                    .Where(r => allUsers.Any(u => u.supervisorId == userId && u.id == r.employeeId)
                             && (r.status == LeaveStatus.pending || r.status == LeaveStatus.approvedBySupervisor))
                    .ToList(),
                UserRole.HOD => allRequests
                    .Where(r => allUsers.Any(u => u.hodId == userId && u.id == r.employeeId)
                             && (r.status == LeaveStatus.approvedBySupervisor || r.status == LeaveStatus.approvedByHod))
                    .ToList(),
                UserRole.HR => allRequests,
                _ => new List<LeaveApplication>()
            };
        }

        // Gets pending leave requests for a supervisor's team
        public async Task<List<LeaveApplication>> GetPendingLeaveRequestsForSupervisorAsync(string supervisorId)
        {
            try
            {
                var users = await GetUsersAsync();
                var supervisedIds = users.Where(u => u.supervisorId == supervisorId).Select(u => u.id).ToList();

                if (!supervisedIds.Any())
                    return new List<LeaveApplication>();

                var allRequests = await GetAllLeaveRequestsAsync();
                var pendingRequests = allRequests
                    .Where(r => supervisedIds.Contains(r.employeeId) && r.status == LeaveStatus.pending)
                    .OrderByDescending(r => r.appliedDate)
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"Supervisor {supervisorId} has {pendingRequests.Count} pending requests for {supervisedIds.Count} supervised users");

                return pendingRequests;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetPendingLeaveRequestsForSupervisorAsync: {ex.Message}");
                return new List<LeaveApplication>();
            }
        }

        // Gets pending leave requests for a HOD's department
        public async Task<List<LeaveApplication>> GetPendingLeaveRequestsForHodAsync(string hodId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Getting pending leaves for HOD: {hodId}");

                var users = await GetUsersAsync();
                var hodUserIds = users.Where(u => u.hodId == hodId).Select(u => u.id).ToList();

                if (!hodUserIds.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"No users found for HOD {hodId}");
                    return new List<LeaveApplication>();
                }

                var allRequests = await GetAllLeaveRequestsAsync();

                // Get requests that are supervisor-approved and ready for HOD review
                var pendingRequests = allRequests
                    .Where(r => hodUserIds.Contains(r.employeeId) &&
                           (r.status == LeaveStatus.approvedBySupervisor ||
                            IsSupervisorApprovedStatus(r.status)))
                    .OrderByDescending(r => r.appliedDate)
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"HOD {hodId} has {pendingRequests.Count} pending requests for {hodUserIds.Count} department users");

                return pendingRequests;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetPendingLeaveRequestsForHodAsync: {ex.Message}");
                return new List<LeaveApplication>();
            }
        }

        // Helper method to check if status indicates supervisor approval
        private bool IsSupervisorApprovedStatus(LeaveStatus status)
        {
            return status == LeaveStatus.approvedBySupervisor ||
                   GetStatusString(status).ToLower().Contains("supervisor");
        }

        // Converts LeaveStatus enum to string representation
        private string GetStatusString(LeaveStatus status)
        {
            return new LeaveStatusConverter().ToFirestore(status)?.ToString() ?? status.ToString();
        }

        // Retrieves all leave requests from Firestore
        public async Task<List<LeaveApplication>> GetAllLeaveRequestsAsync()
        {
            System.Diagnostics.Debug.WriteLine("=== Starting GetAllLeaveRequests (Manual Conversion) ===");

            try
            {
                var snapshot = await _firestoreDb.Collection("leaveApplications")
                    .OrderByDescending("appliedDate")
                    .GetSnapshotAsync();

                System.Diagnostics.Debug.WriteLine($"Found {snapshot.Documents.Count} total leave applications");

                var allLeaveApplications = new List<LeaveApplication>();
                var userCache = new Dictionary<string, User>(); // Cache users to avoid repeated queries

                foreach (var doc in snapshot.Documents)
                {
                    try
                    {
                        var data = doc.ToDictionary();
                        var leaveApp = MapDictionaryToLeaveApplication(data, doc.Id);

                        // Get user data with caching for performance
                        if (!string.IsNullOrEmpty(leaveApp.employeeId))
                        {
                            if (!userCache.TryGetValue(leaveApp.employeeId, out var user))
                            {
                                user = await GetUserByIdAsync(leaveApp.employeeId);
                                userCache[leaveApp.employeeId] = user;
                            }
                            leaveApp.User = user;
                        }

                        allLeaveApplications.Add(leaveApp);
                        System.Diagnostics.Debug.WriteLine($"Added leave application {doc.Id} for employee {leaveApp.employeeId} with status: {leaveApp.status}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error converting document {doc.Id}: {ex.Message}");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"=== Completed GetAllLeaveRequests. Total: {allLeaveApplications.Count} applications ===");
                return allLeaveApplications;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetAllLeaveRequests: {ex.Message}");
                return new List<LeaveApplication>();
            }
        }

        // Gets all approved leave requests
        public async Task<List<LeaveApplication>> GetApprovedLeaveRequestsAsync()
        {
            var allRequests = await GetAllLeaveRequestsAsync();
            return allRequests
                .Where(r => r.status == LeaveStatus.approvedBySupervisor || r.status == LeaveStatus.approvedByHod)
                .ToList();
        }

        // Gets leave requests by department
        public async Task<List<LeaveApplication>> GetLeaveRequestsByDepartmentAsync(string department)
        {
            var all = await GetAllLeaveRequestsAsync();
            return all.Where(r => r.User?.department == department).ToList();
        }

        // Updates a leave request in Firestore
        public async Task UpdateLeaveRequestAsync(LeaveApplication leaveRequest)
        {
            var docRef = _firestoreDb.Collection("leaveApplications").Document(leaveRequest.id);

            var updateData = new Dictionary<string, object>
            {
                {"status", leaveRequest.status.ToString() },
                {"lastUpdated", Timestamp.GetCurrentTimestamp() }
            };

            // Add optional supervisor/HOD feedback if provided
            if (!string.IsNullOrEmpty(leaveRequest.supervisorFeedback))
                updateData["SupervisorRemarks"] = leaveRequest.supervisorFeedback;

            if (!string.IsNullOrEmpty(leaveRequest.hodFeedback))
                updateData["HodRemarks"] = leaveRequest.hodFeedback;

            if (leaveRequest.supervisorActionDate != default)
                updateData["SupervisorActionDate"] = leaveRequest.supervisorActionDate;

            if (leaveRequest.hodActionDate != default)
                updateData["HodActionDate"] = leaveRequest.hodActionDate;

            await docRef.UpdateAsync(updateData);
        }

        // Marks leave as recorded by HR and updates leave balance
        public async Task CaptureLeaveAsync(string leaveRequestId, string hrUserId)
        {
            var leave = await GetLeaveRequestByIdAsync(leaveRequestId);
            if (leave == null) return;

            // Update leave balance
            await UpdateUserLeaveBalanceAsync(leave.employeeId, leave.type, leave.numberOfDays, false);

            // Update leave status to recorded
            var updateData = new Dictionary<string, object>
            {
                {"status", LeaveStatus.recorded.ToString() },
                {"CapturedBy", hrUserId },
                {"CapturedAt", DateTime.UtcNow },
                {"lastUpdated", Timestamp.GetCurrentTimestamp() }
            };

            await _firestoreDb.Collection("leaveApplications").Document(leaveRequestId).UpdateAsync(updateData);
        }

        // Diagnostic method to inspect Firestore data
        public async Task CheckFirestoreDataAsync(string userId)
        {
            try
            {
                // Check user document
                var userDoc = await _firestoreDb.Collection("users").Document(userId).GetSnapshotAsync();
                System.Diagnostics.Debug.WriteLine($"User document exists: {userDoc.Exists}");

                if (userDoc.Exists)
                {
                    var userData = userDoc.ToDictionary();
                    System.Diagnostics.Debug.WriteLine($"User data: {string.Join(", ", userData.Select(kv => $"{kv.Key}: {kv.Value}"))}");
                }

                // Check leave applications for this user
                var leaveQuery = _firestoreDb.Collection("leaveApplications")
                    .WhereEqualTo("employeeId", userId);
                var leaveSnapshot = await leaveQuery.GetSnapshotAsync();

                System.Diagnostics.Debug.WriteLine($"Found {leaveSnapshot.Documents.Count} leave applications for employeeId: {userId}");

                foreach (var doc in leaveSnapshot.Documents)
                {
                    var data = doc.ToDictionary();
                    System.Diagnostics.Debug.WriteLine($"Leave App {doc.Id}: {string.Join(", ", data.Select(kv => $"{kv.Key}: {kv.Value}"))}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Diagnostic error: {ex.Message}");
            }
        }

        // Alias for GetUserByEmailAsync
        public async Task<User> GetUserProfileAsync(string email)
        {
            return await GetUserByEmailAsync(email);
        }

        // Updates user profile information
        public async Task<bool> UpdateUserProfileAsync(string email, string firstName, string lastName, string phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            try
            {
                // Find user by email
                var query = _firestoreDb.Collection("users")
                    .WhereEqualTo("email", email)
                    .Limit(1);

                var snapshot = await query.GetSnapshotAsync();
                var doc = snapshot.Documents.FirstOrDefault();

                if (doc == null)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ UpdateUserProfileAsync: No user found for email {email}");
                    return false;
                }

                var updateData = new Dictionary<string, object>();

                // Only update provided fields
                if (!string.IsNullOrWhiteSpace(firstName))
                    updateData["firstName"] = firstName;

                if (!string.IsNullOrWhiteSpace(lastName))
                    updateData["lastName"] = lastName;

                if (!string.IsNullOrWhiteSpace(phoneNumber))
                    updateData["phoneNumber"] = phoneNumber;

                if (!updateData.Any())
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ UpdateUserProfileAsync: No fields to update");
                    return true;
                }

                await doc.Reference.UpdateAsync(updateData);
                System.Diagnostics.Debug.WriteLine($"✅ Profile updated for {email}");

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in UpdateUserProfileAsync: {ex.Message}");
                return false;
            }
        }

        // Gets all employee IDs from Firestore
        public async Task<List<string>> GetAllEmployeeIds()
        {
            try
            {
                var snapshot = await _firestoreDb.Collection("users").GetSnapshotAsync();
                var employeeIds = snapshot.Documents
                    .Where(doc => doc.Exists)
                    .Select(doc => doc.Id)
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"Found {employeeIds.Count} employee IDs");
                return employeeIds;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting employee IDs: {ex.Message}");
                return new List<string>();
            }
        }

        // Alias for GetAllLeaveRequestsAsync
        public async Task<List<LeaveApplication>> GetAllLeaveRequests()
        {
            return await GetAllLeaveRequestsAsync();
        }


        public FirestoreChangeListener CreateLeaveRequestsListener(string userId, UserRole role, Action<QuerySnapshot> onSnapshot)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Creating real-time listener for role {role} - User: {userId}");

                Query query = role switch
                {
                    UserRole.Employee => _firestoreDb.Collection("leaveApplications")
                        .WhereEqualTo("employeeId", userId)
                        .OrderByDescending("appliedDate"),

                    UserRole.Supervisor => _firestoreDb.Collection("leaveApplications")
                        .WhereEqualTo("status", "pending")
                        .OrderByDescending("appliedDate"),

                    UserRole.HOD => _firestoreDb.Collection("leaveApplications")
                        .WhereIn("status", new[] { "pending", "approvedBySupervisor" })
                        .OrderByDescending("appliedDate"),

                    UserRole.HR => _firestoreDb.Collection("leaveApplications")
                        .OrderByDescending("appliedDate"),

                    _ => _firestoreDb.Collection("leaveApplications")
                        .WhereEqualTo("employeeId", userId)
                        .OrderByDescending("appliedDate")
                };

                var listener = query.Listen(snapshot =>
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"Real-time update received: {snapshot.Documents.Count} documents");
                        onSnapshot(snapshot);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error in real-time listener: {ex}");
                    }
                });

                System.Diagnostics.Debug.WriteLine("Real-time listener created successfully");
                return listener;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating listener: {ex}");
                throw;
            }
        }



        // Maps Firestore dictionary to LeaveApplication object
        private LeaveApplication MapDictionaryToLeaveApplication(Dictionary<string, object> data, string documentId)
        {
            var leaveApp = new LeaveApplication
            {
                id = documentId,
                employeeId = data.GetValueOrDefault("employeeId")?.ToString(),
                employeeName = data.GetValueOrDefault("employeeName")?.ToString(),
                reason = data.GetValueOrDefault("reason")?.ToString(),
                numberOfDays = data.ContainsKey("numberOfDays") ? Convert.ToInt32(data["numberOfDays"]) : 0
            };

            // Handle timestamp conversions
            if (data.ContainsKey("appliedDate") && data["appliedDate"] is Timestamp appliedTs)
                leaveApp.appliedDate = appliedTs.ToDateTime();

            if (data.ContainsKey("startDate") && data["startDate"] is Timestamp startTs)
                leaveApp.startDate = startTs.ToDateTime();

            if (data.ContainsKey("endDate") && data["endDate"] is Timestamp endTs)
                leaveApp.endDate = endTs.ToDateTime();

            if (data.ContainsKey("SupervisorActionDate") && data["SupervisorActionDate"] is Timestamp supervisorActionTs)
                leaveApp.supervisorActionDate = supervisorActionTs.ToDateTime();

            if (data.ContainsKey("HodActionDate") && data["HodActionDate"] is Timestamp hodActionTs)
                leaveApp.hodActionDate = hodActionTs.ToDateTime();

            if (data.ContainsKey("CapturedAt") && data["CapturedAt"] is Timestamp capturedTs)
                leaveApp.CapturedAt = capturedTs.ToDateTime();

            // Convert string types to enums using custom converters
            if (data.ContainsKey("type"))
            {
                var typeString = data["type"]?.ToString();
                leaveApp.type = new LeaveTypeConverter().FromFirestore(typeString);
            }

            if (data.ContainsKey("status"))
            {
                var statusString = data["status"]?.ToString();
                leaveApp.status = new LeaveStatusConverter().FromFirestore(statusString);
            }

            // Map feedback fields
            leaveApp.supervisorFeedback = data.GetValueOrDefault("SupervisorRemarks")?.ToString();
            leaveApp.hodFeedback = data.GetValueOrDefault("HodRemarks")?.ToString();
            leaveApp.CapturedBy = data.GetValueOrDefault("CapturedBy")?.ToString();

            return leaveApp;
        }

        // Cancels a leave request and restores leave balance
        public async Task<bool> CancelLeaveAsync(string leaveRequestId)
        {
            var leave = await GetLeaveRequestByIdAsync(leaveRequestId);
            if (leave == null) return false;

            // Update status to cancelled
            var updateData = new Dictionary<string, object>
            {
                {"status", new LeaveStatusConverter().ToFirestore(LeaveStatus.cancelled)},
                {"lastUpdated", Timestamp.GetCurrentTimestamp()}
            };

            await _firestoreDb.Collection("leaveApplications")
                .Document(leaveRequestId)
                .UpdateAsync(updateData);

            System.Diagnostics.Debug.WriteLine($"✅ Leave cancelled & balance restored for: {leaveRequestId}");
            return true;
        }

        // Gets raw Firestore document data for debugging
        public async Task<Dictionary<string, object>> GetRawDocumentAsync(string collection, string id)
        {
            var docRef = _firestoreDb.Collection(collection).Document(id);
            var snapshot = await docRef.GetSnapshotAsync();

            return snapshot.Exists ? snapshot.ToDictionary() : null;
        }
        public async Task<User> GetSupervisorForEmployeeAsync(string employeeId)
        {
            var employee = await GetUserByIdAsync(employeeId);
            if (employee == null) return null;


            // Try SupervisorId first
            var supId = GetStringProperty(employee, "SupervisorId") ?? GetStringProperty(employee, "supervisorId");
            if (!string.IsNullOrEmpty(supId))
            {
                return await GetUserByIdAsync(supId);
            }


            // Fallback: SupervisorEmail
            var supEmail = GetStringProperty(employee, "SupervisorEmail") ?? GetStringProperty(employee, "supervisor_email");
            if (!string.IsNullOrEmpty(supEmail)) return await GetUserByEmailAsync(supEmail);


            return null;
        }


        public async Task<User> GetHodForEmployeeAsync(string employeeId)
        {
            var employee = await GetUserByIdAsync(employeeId);
            if (employee == null) return null;


            var hodId = GetStringProperty(employee, "HodId") ?? GetStringProperty(employee, "hodId");
            if (!string.IsNullOrEmpty(hodId)) return await GetUserByIdAsync(hodId);


            var hodEmail = GetStringProperty(employee, "HodEmail") ?? GetStringProperty(employee, "hod_email");
            if (!string.IsNullOrEmpty(hodEmail)) return await GetUserByEmailAsync(hodEmail);


            return null;
        }


        public async Task<User> GetHrForEmployeeAsync(string employeeId)
        {
            var employee = await GetUserByIdAsync(employeeId);
            if (employee == null) return null;


            var hrId = GetStringProperty(employee, "HrId") ?? GetStringProperty(employee, "hrId");
            if (!string.IsNullOrEmpty(hrId)) return await GetUserByIdAsync(hrId);


            var hrEmail = GetStringProperty(employee, "HrEmail") ?? GetStringProperty(employee, "hr_email");
            if (!string.IsNullOrEmpty(hrEmail)) return await GetUserByEmailAsync(hrEmail);


            return null;
        }
        private string GetStringProperty(object obj, string propName)
        {
            if (obj == null) return null;
            var p = obj.GetType().GetProperty(propName);
            if (p == null) return null;
            var val = p.GetValue(obj);
            return val?.ToString();
        }
    }
}