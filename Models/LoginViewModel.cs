using System.ComponentModel.DataAnnotations;

namespace Leave2Day.Models
{
    public class LoginViewModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } // User's email address

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } // User's password
    }
}