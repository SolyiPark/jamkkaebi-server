package com.example.jamkkaebi.friend.dto.response;

import com.example.jamkkaebi.friend.domain.FriendRelation;
import com.fasterxml.jackson.annotation.JsonInclude;

import java.time.LocalDateTime;

/**
 * 친구 요청 보내기·수락의 결과.
 *
 * @param relation  결과 관계
 * @param player    상대 요약
 * @param expiresAt 요청 만료 시각 — 친구가 됐으면 키 생략
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record FriendRequestResponse(FriendRelation relation, PlayerCard player, LocalDateTime expiresAt) {

    public static FriendRequestResponse sent(PlayerCard player, LocalDateTime expiresAt) {
        return new FriendRequestResponse(FriendRelation.REQUEST_SENT, player, expiresAt);
    }

    public static FriendRequestResponse friend(PlayerCard player) {
        return new FriendRequestResponse(FriendRelation.FRIEND, player, null);
    }
}
