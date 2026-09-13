package com.example.jamkkaebi.friend.dto.response;

/**
 * 방문 보상 결과.
 *
 * @param jeongseongGained   이번 호출로 받은 정성. 오늘 이미 받았으면 0
 * @param visitRewardClaimed 호출 후 오늘 보상 수령 여부
 */
public record VisitRewardResponse(int jeongseongGained, boolean visitRewardClaimed) {
}
