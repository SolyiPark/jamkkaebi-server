package com.example.jamkkaebi.friend.dto.request;

import jakarta.validation.constraints.NotBlank;

/**
 * 친구 코드를 담는 요청으로 친구 요청 보내기와 차단이 쓴다.
 *
 * <p>코드 형식은 여기서 막지 않고 서비스가 정규화한 뒤 검사한다.
 *
 * @param friendCode 상대 친구 코드 ({@code K7QM-3PVX}, {@code k7qm3pvx} 모두 허용)
 */
public record FriendCodeRequest(@NotBlank String friendCode) {
}
