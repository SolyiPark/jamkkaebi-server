package com.example.jamkkaebi.user.dto.request;

import jakarta.validation.constraints.NotBlank;

/**
 * 닉네임 변경 요청.
 *
 * <p>길이·금칙어 기준은 {@code NicknamePolicy} 한곳에서 판단한다. 여기에 {@code @Size} 를 겹쳐 두면
 * 기준이 두 군데로 갈라져, 한쪽만 고쳤을 때 소셜 가입과 수정 화면이 서로 다른 이름을 허용하게 된다.
 *
 * @param nickname 새 닉네임
 */
public record NicknameUpdateRequest(@NotBlank String nickname) {
}
