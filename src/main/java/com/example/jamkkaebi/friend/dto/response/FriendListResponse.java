package com.example.jamkkaebi.friend.dto.response;

import com.example.jamkkaebi.friend.domain.GiftState;
import com.fasterxml.jackson.annotation.JsonUnwrapped;

import java.time.LocalDateTime;
import java.util.List;

/**
 * 친구 탭 첫 화면.
 *
 * @param myFriendCode         내 친구 코드
 * @param count                친구 수
 * @param limit                친구 상한
 * @param friends              친구 목록, 최근 접속순
 * @param receivedRequestCount 응답을 기다리는 받은 요청 수
 * @param today                오늘의 소셜 상태
 * @param nextResetAt          다음 게임 일자 시작 시각
 */
public record FriendListResponse(
        String myFriendCode,
        int count,
        int limit,
        List<FriendSummary> friends,
        long receivedRequestCount,
        Today today,
        LocalDateTime nextResetAt
) {

    /**
     * @param player            친구 요약
     * @param lastActiveDaysAgo 마지막 접속 경과 일수 (0 = 오늘)
     * @param visitedToday      오늘 이 친구 전시관을 방문했는지
     * @param giftState         선물 버튼 상태
     */
    public record FriendSummary(
            @JsonUnwrapped PlayerCard player,
            long lastActiveDaysAgo,
            boolean visitedToday,
            GiftState giftState
    ) {
    }

    public record Today(boolean visitRewardClaimed, boolean giftSent, boolean giftTicketAvailable) {
    }
}
