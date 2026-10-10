package com.example.jamkkaebi.exhibition.dto.response;

import com.example.jamkkaebi.friend.spi.ExhibitionView;

import java.util.List;

/**
 * 내 전시관.
 *
 * <p>배치하지 않은 항목도 담는다 — 배치 편집 화면이 둘 수 있는 것 전체를 보여 줘야 한다.
 * 숨긴 도깨비도 목록에 남는다.
 *
 * @param grid      배치 좌표 범위
 * @param spirits   정령 수호까지 마친 도깨비 전부 ({@code artifactId} 순)
 * @param buildings 받은 건물 전부 ({@code buildingId} 순)
 */
public record ExhibitionResponse(
        ExhibitionView.Grid grid,
        List<ExhibitSpiritView> spirits,
        List<ExhibitBuildingView> buildings
) {
}
