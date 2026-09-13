package com.example.jamkkaebi.friend.dto.response;

import com.example.jamkkaebi.friend.domain.FriendRelation;

import java.util.List;

/**
 * 친구 코드 조회 결과.
 *
 * @param player        상대 요약
 * @param restoredByEra 시대별 완료 유물 수 (0인 시대는 생략)
 * @param relation      나와의 관계
 */
public record FriendLookupResponse(PlayerCard player, List<EraCount> restoredByEra, FriendRelation relation) {
}
