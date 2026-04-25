using System;
using System.Threading.Tasks;
using Orka.Core.DTOs.Auth;
using Orka.Core.Entities;

namespace Orka.Core.Interfaces;

public interface IAuthService
{
    Task<(string Token, string RefreshToken, User User)> RegisterAsync(string firstName, string lastName, string email, string password, UserProfileDraft? profile = null);
    Task<(string Token, string RefreshToken, User User)> LoginAsync(string email, string password);
    Task<(string Token, string RefreshToken)> RefreshAsync(string refreshToken);
    Task RevokeAsync(string refreshToken);
}
