package com.example.jamkkaebi.exhibition.dto.response;

import com.example.jamkkaebi.friend.spi.ExhibitionView;

import java.time.LocalDate;
import java.time.LocalDateTime;
import java.util.List;

/**
 * 내 전시관.
 *
 * <p>배치하지 않은 항목도 담는다 — 배치 편집 화면이 둘 수 있는 것 전체를 보여 줘야 한다.
 * 숨긴 도깨비도 목록에 남는다.
 *
 * @param grid      배치 좌표 범위
 * @param spirits   오염 관리까지 마친 도깨비 전부 ({@code relicId} 순)
 * @param buildings 받은 건물 전부 ({@code buildingId} 순)
 * @param today     오늘 게임 일자 ({@link com.example.jamkkaebi.global.time.GameClock}, KST 자정에 바뀐다).
 *                  선명도를 계산한 시각과 같은 날이다. 클라이언트는 도깨비 대화의 "며칠 전에 닦아 줬다"
 *                  같은 날짜 계산을 기기 시계 대신 이 값으로 한다
 * @param now       서버 시각(KST, {@link com.example.jamkkaebi.global.time.GameClock#now()}, {@code today} 와 같은 시각에서 뗀 것).
 *                  도깨비가 탭 인사에서 새벽·아침·낮·저녁·밤 같은 시간대를 말하는 데 쓴다. 기기 시계를 쓰지 않는다
 * @param nickname  내 닉네임. 도깨비가 나를 부를 때 쓴다 (친밀할 때만 — 최근 30일 방문 3번 이상. 아니면 '발굴자')
 */
public record ExhibitionResponse(
        ExhibitionView.Grid grid,
        List<ExhibitSpiritView> spirits,
        List<ExhibitBuildingView> buildings,
        LocalDate today,
        LocalDateTime now,
        String nickname
) {
}
