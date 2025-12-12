using Leave2Day.Models;
using Leave2Day.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Google.Cloud.Firestore;

namespace Leave2Day.Controllers
{
    public class AccountController : Controller
    {
        private readonly IFirebaseService _firebaseService;

        // Constructor: Dependency injection for Firebase service
        public AccountController(IFirebaseService firebaseService)
        {
            _firebaseService = firebaseService;
        }

        // GET: Display login form
        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        // POST: Process login request
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            try
            {
                // Authenticate user with Firebase
                var user = await _firebaseService.AuthenticateUserAsync(model.Email, model.Password);

                if (user == null)
                {
                    ModelState.AddModelError("", "Invalid email or password.");
                    return View(model);
                }

                // Create claims for authenticated user
                var claims = new List<Claim>
                {
                   new Claim(ClaimTypes.NameIdentifier, user.id),
                   new Claim(ClaimTypes.Name, user.FullName),
                   new Claim(ClaimTypes.Email, user.email),
                   new Claim(ClaimTypes.Role, user.role.ToString()), // Standard role claim
                   new Claim("Role", user.role.ToString()), // Custom role claim
                   new Claim("Department", user.department)
                };

                // Sign in user with cookie authentication
                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(24) // 24-hour session
                };

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity), authProperties);

                return RedirectToAction("Index", "Dashboard");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Login failed: {ex.Message}");
                return View(model);
            }
        }

        // POST: Log out user
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }
    }
}