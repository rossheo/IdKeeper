using Microsoft.AspNetCore.Identity;

namespace IdKeeper.Web.Components.Account
{
	// 실제 이메일 발송기 없이 링크를 화면에 노출하던 방식 대신, 이메일 확인은
	// UserRoles 페이지에서 관리자가 EmailConfirmed를 수동으로 승인하는 흐름을 사용한다.
	internal sealed class IdentityNoOpEmailSender : IEmailSender<IdentityUser>
	{
		public Task SendConfirmationLinkAsync(IdentityUser user, string email, string confirmationLink) =>
			Task.CompletedTask;

		public Task SendPasswordResetLinkAsync(IdentityUser user, string email, string resetLink) =>
			Task.CompletedTask;

		public Task SendPasswordResetCodeAsync(IdentityUser user, string email, string resetCode) =>
			Task.CompletedTask;
	}
}
