package com.example.jamkkaebi.friend.dto.response;

import java.time.LocalDateTime;
import java.util.List;

/**
 * 내가 차단한 목록, 최근 순.
 *
 * @param blocks 차단한 사용자
 */
public record BlockListResponse(List<BlockedUser> blocks) {

    public record BlockedUser(String friendCode, String nickname, LocalDateTime blockedAt) {
    }
}
