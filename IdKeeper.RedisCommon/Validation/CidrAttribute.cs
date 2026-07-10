using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Sockets;

namespace IdKeeper.Database.Redis.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class CidrAttribute : ValidationAttribute
{
	public CidrAttribute()
	{
		ErrorMessage = "유효한 IPv4/IPv6 CIDR(예: 192.168.1.0/24, 2001:db8::/32) 형식이어야 합니다.";
	}

	protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
	{
		if (value is null)
		{
			return ValidationResult.Success; // [Required]가 별도로 처리
		}

		if (value is not string s)
		{
			return new ValidationResult(ErrorMessage);
		}

		s = s.Trim();
		if (s.Length == 0)
		{
			return new ValidationResult(ErrorMessage);
		}

		var parts = s.Split('/', 2, StringSplitOptions.TrimEntries);
		if (parts.Length != 2)
		{
			return new ValidationResult(ErrorMessage);
		}

		if (!IPAddress.TryParse(parts[0], out var ip))
		{
			return new ValidationResult(ErrorMessage);
		}

		if (!Int32.TryParse(parts[1], out Int32 prefix))
		{
			return new ValidationResult(ErrorMessage);
		}

		bool ok = ip.AddressFamily switch
		{
			AddressFamily.InterNetwork => prefix is >= 0 and <= 32,
			AddressFamily.InterNetworkV6 => prefix is >= 0 and <= 128,
			_ => false
		};

		return ok ? ValidationResult.Success : new ValidationResult(ErrorMessage);
	}
}