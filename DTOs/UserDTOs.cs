using System.ComponentModel.DataAnnotations;
using GamesDatabase.Api.Models;

namespace GamesDatabase.Api.DTOs;

public class LoginRequest
{
    [Required]
    public string Username { get; set; } = string.Empty;

    public string? Password { get; set; }
}

public class LoginResponse
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}

public class CreateUserRequest
{
    [Required]
    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    public string? Password { get; set; }

    [Required]
    public string Role { get; set; } = "Standard";
}

public class UpdateUserRequest
{
    [MaxLength(50)]
    public string? Username { get; set; }

    public string? Role { get; set; }

    public bool? UseScoreColors { get; set; }

    public string? ScoreProvider { get; set; }

    public bool? ShowPriceComparisonIcon { get; set; }
}

public class ChangePasswordRequest
{
    [Required]
    public string? NewPassword { get; set; }
}

public class UserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool HasPassword { get; set; }
    public bool UseScoreColors { get; set; }
    public string ScoreProvider { get; set; } = "Metacritic";
    public bool ShowPriceComparisonIcon { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
