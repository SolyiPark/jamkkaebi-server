package com.example.jamkkaebi.user.dto.response;

import com.example.jamkkaebi.user.domain.User;

/**
 * 내 프로필 응답.
 *
 * <p>{@code userId} 와 {@code providerUserId} 는 담지 않는다 — 밖으로 나가는 식별자는 친구 코드
 * 하나뿐이다.
 *
 * @param friendCode 친구 코드
 * @param nickname   닉네임
 * @param spiritId   프로필 아바타 식별자 ({@code null} 이면 기본 사람 모양)
 */
public record MyProfileResponse(
        String friendCode,
        String nickname,
        Long spiritId
) {

    public static MyProfileResponse from(User user) {
        return new MyProfileResponse(user.getFriendCode(), user.getNickname(), user.getSpiritId());
    }
}
