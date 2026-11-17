package com.example.jamkkaebi.friend.dto.response;

import com.example.jamkkaebi.friend.domain.GiftState;
import com.example.jamkkaebi.friend.spi.ExhibitionView;
import com.fasterxml.jackson.annotation.JsonInclude;

import java.time.LocalDate;
import java.time.LocalDateTime;
import java.util.List;

/**
 * 친구 전시관 — 친구가 마지막으로 접속했을 때의 스냅샷.
 *
 * @param owner                전시관 주인
 * @param grid                 배치 좌표 범위
 * @param spirits              배치되고 숨기지 않은 도깨비
 * @param buildings            배치된 건물
 * @param capturedAt           마지막 접속 시각
 * @param giftState            선물 버튼 상태
 * @param visitRewardAvailable 오늘 방문 보상을 아직 받지 않았는지
 * @param recentVisitDays      최근 30일 동안 내가 이 친구 전시관을 방문한 날 수
 * @param visitorNickname      지금 열람한 나(방문객)의 닉네임. 이 전시관의 도깨비가 나를 부를 때 쓴다
 * @param recentVisitDates     최근 30일(오늘 포함) 동안 내가 이 친구 전시관을 방문한 날들, 가까운 날부터.
 *                             {@code recentVisitDays} 는 이 목록의 길이다. 도깨비가 "며칠 전에 왔다"를 실제 기록대로 말하는 데 쓴다
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record FriendExhibitionResponse(
        PlayerCard owner,
        ExhibitionView.Grid grid,
        List<ExhibitionView.SpiritView> spirits,
        List<ExhibitionView.BuildingView> buildings,
        LocalDateTime capturedAt,
        GiftState giftState,
        boolean visitRewardAvailable,
        int recentVisitDays,
        String visitorNickname,
        List<LocalDate> recentVisitDates
) {
}
