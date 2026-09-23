package com.example.jamkkaebi.workbench.dto.response;

import com.example.jamkkaebi.artifact.dto.response.BuildingView;

import java.util.List;

/**
 * 작업대 첫 화면.
 *
 * <p>도깨비 목록은 <b>돌봐야 할 순서</b>로 정렬한다 — 재봉인 → 흐려짐 → 성장 가능 → 나머지
 *
 * @param capacity    미확인 슬롯 수
 * @param slots       아직 정령 수호를 마치지 않은 유물. 획득 순
 * @param lockedCount 아직 뽑지 못한 유물 수
 */
public record WorkbenchResponse(
        int capacity,
        List<SlotView> slots,
        List<SpiritView> spirits,
        int lockedCount,
        List<BuildingView> buildings
) {
}
