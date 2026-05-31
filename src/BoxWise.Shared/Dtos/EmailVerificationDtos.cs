namespace BoxWise.Shared.Dtos;

public record SendEmailCodeRequest(string Email);
public record VerifyEmailCodeRequest(string Code, string Token);
public record EmailVerificationSendResponse(string Token);
public record EmailVerifyCodeResponse(string OperationToken, string VerifiedEmail);
