package com.example.jamkkaebi.box.dto.response;

import com.fasterxml.jackson.annotation.JsonInclude;

import java.time.LocalDateTime;

/**
 * 전시관 최상단 "오늘의 상자" 상태.
 *
 * <p>열 수 있는 경로가 셋이라 각각의 가능 여부를 따로 준다. 작업대가 가득 차면 <b>세 경로가 모두</b>
 * 잠긴다.
 *
 * @param nextResetAt 다음 게임 일자가 시작되는 시각.
 */
public record BoxStatusResponse(
        FreeBox free,
        GiftBox gift,
        PurchaseBox purchase,
        WorkbenchStatus workbench,
        LocalDateTime nextResetAt
) {

    /**
     * @param nextOpenAt 다음 무료 상자 시각. 지금 열 수 있으면 {@code null}
     */
    @JsonInclude(JsonInclude.Include.NON_NULL)
    public record FreeBox(boolean available, LocalDateTime nextOpenAt) {
    }

    // 미사용 선물 상자권 보유 여부. 하루 최대 한 장
    public record GiftBox(boolean available) {
    }

    /**
     * @param price      정성 구매가. 구매 경로를 닫으면 {@code null}
     * @param dailyLimit 1일 구매 상한. 상한이 없으면 {@code null}
     */
    @JsonInclude(JsonInclude.Include.NON_NULL)
    public record PurchaseBox(Integer price, int todayCount, Integer dailyLimit) {
    }

    /**
     * @param full {@code true} 면 세 경로의 버튼을 모두 잠그고 정화 플레이로 유도한다
     */
    public record WorkbenchStatus(int inProgress, int capacity, boolean full) {
    }
}
