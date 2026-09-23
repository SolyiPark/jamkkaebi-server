package com.example.jamkkaebi.friend.dto.response;

import com.example.jamkkaebi.friend.domain.GiftState;
import com.example.jamkkaebi.friend.spi.ExhibitionView;
import com.fasterxml.jackson.annotation.JsonInclude;

import java.time.LocalDateTime;
import java.util.List;

/**
 * 친구 전시관 — 친구가 마지막으로 접속했을 때의 스냅샷.
 *
 * @param owner                전시관 주인
 * @param grid                 배치 좌표 범위 — 스냅샷이 없으면 키 생략
 * @param spirits              배치되고 숨기지 않은 도깨비
 * @param buildings            배치된 건물
 * @param capturedAt           스냅샷을 찍은 시각(주인의 마지막 접속) — 스냅샷이 없으면 키 생략
 * @param giftState            선물 버튼 상태
 * @param visitRewardAvailable 오늘 방문 보상을 아직 받지 않았는지
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record FriendExhibitionResponse(
        PlayerCard owner,
        ExhibitionView.Grid grid,
        List<ExhibitionView.SpiritView> spirits,
        List<ExhibitionView.BuildingView> buildings,
        LocalDateTime capturedAt,
        GiftState giftState,
        boolean visitRewardAvailable
) {
}
