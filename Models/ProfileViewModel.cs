using System.ComponentModel.DataAnnotations;

namespace Leave2Day.Models
{
    public class ProfileViewModel
    {
        [Display(Name = "Name")]
        public string FirstName { get; set; } = ""; // User's first name

        [Required(ErrorMessage = "Surname is required")]
        [Display(Name = "Surname")]
        public string LastName { get; set; } = ""; // User's last name (required)

        [Display(Name = "Email")]
        [EmailAddress]
        public string Email { get; set; } = ""; // User's email address

        [Display(Name = "Phone Number")]
        [Phone(ErrorMessage = "Enter a valid phone number")]
        public string PhoneNumber { get; set; } = ""; // User's phone number
    }
}