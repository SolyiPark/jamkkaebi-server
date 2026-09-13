package com.example.jamkkaebi.friend.dto.response;

import com.example.jamkkaebi.friend.domain.RequestDirection;

import java.time.LocalDateTime;
import java.util.List;

/**
 * 요청함.
 *
 * @param direction 조회한 방향
 * @param count     대기 요청 수
 * @param limit     상한 (받은 20 / 보낸 10)
 * @param requests  요청 목록, 최신순
 */
public record FriendRequestListResponse(
        RequestDirection direction,
        int count,
        int limit,
        List<RequestItem> requests
) {

    /**
     * @param player    상대 요약
     * @param createdAt 요청 시각
     * @param expiresAt 만료 시각
     */
    public record RequestItem(PlayerCard player, LocalDateTime createdAt, LocalDateTime expiresAt) {
    }
}
