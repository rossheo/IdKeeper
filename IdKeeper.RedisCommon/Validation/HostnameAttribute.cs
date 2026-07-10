using System.ComponentModel.DataAnnotations;

namespace IdKeeper.Database.Redis.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class HostnameAttribute : ValidationAttribute
{
	public HostnameAttribute()
	{
		ErrorMessage = "유효한 호스트명(DDNS) 형식이어야 합니다. (예: myhome.duckdns.org)";
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

		// IP 주소는 Cidr 필드를 쓰도록 유도하기 위해 순수 DNS 이름만 허용한다.
		return Uri.CheckHostName(s) == UriHostNameType.Dns
			? ValidationResult.Success
			: new ValidationResult(ErrorMessage);
	}
}
