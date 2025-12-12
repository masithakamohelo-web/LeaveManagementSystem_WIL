using Leave2Day.Models;
using Leave2Day.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Leave2Day.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly IFirebaseService _firebaseService;

        public ProfileController(IFirebaseService firebaseService)
        {
            _firebaseService = firebaseService;
        }

        // GET: /Profile/Manage
        [HttpGet]
        public async Task<IActionResult> Manage()
        {
            string emailClaim = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(emailClaim))
                return Unauthorized();

            // Try to get the user from Firestore
            var user = await _firebaseService.GetUserProfileAsync(emailClaim)
                       ?? await _firebaseService.GetUserByEmailAsync(emailClaim);

            // If user not found, create a minimal display user
            if (user == null)
            {
                user = new User
                {
                    email = emailClaim,
                    firstName = User.Identity?.Name ?? "",
                    lastName = "",
                    // ⚠️ This is just a fallback for display; real value should come from Firestore
                    phoneNumber = ""
                };
            }

            var model = new ProfileViewModel
            {
                FirstName = user.firstName ?? (User.Identity?.Name ?? ""),
                LastName = user.lastName ?? "",
                Email = user.email ?? emailClaim,

                // ✅ Make sure this matches your User model property
                // If your property is PhoneNumber (capital P), change this line accordingly.
                PhoneNumber = user.phoneNumber ?? ""
            };

            Debug.WriteLine($"[Profile GET] Loaded phone='{model.PhoneNumber}' for email={model.Email}");
            return View(model);
        }

        // POST: /Profile/Manage
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Manage(ProfileViewModel model)
        {
            // preserve read-only / derived fields if validation fails
            if (!ModelState.IsValid)
            {
                foreach (var kv in ModelState)
                {
                    if (kv.Value.Errors.Count > 0)
                        Debug.WriteLine($"ModelState error for '{kv.Key}': {string.Join(", ", kv.Value.Errors)}");
                }

                model.Email ??= User.FindFirstValue(ClaimTypes.Email);
                // Only fall back to Identity name if null (not if user typed something invalid)
                model.FirstName ??= User.Identity?.Name ?? "";
                return View(model);
            }

            string emailClaim = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(emailClaim))
                return Unauthorized();

            // ❌ Don't force lowercase here unless you *also* store emails lowercase in Firestore.
            // Just use the same value as when you saved the user.
            var emailForUpdate = emailClaim;

            Debug.WriteLine($"[Profile POST] Updating profile for {emailForUpdate} " +
                            $"FirstName='{model.FirstName}', LastName='{model.LastName}', Phone='{model.PhoneNumber}'");

            // ✅ Call the 4-parameter update (email, firstName, lastName, phoneNumber)
            bool ok = await _firebaseService.UpdateUserProfileAsync(
                emailForUpdate,
                model.FirstName,
                model.LastName,
                model.PhoneNumber
            );

            if (ok)
            {
                TempData["ProfileSuccess"] = "Profile updated successfully!";
            }
            else
            {
                TempData["ProfileError"] = "Failed to update profile. Please try again.";
            }

            return RedirectToAction(nameof(Manage));
        }
    }
}
