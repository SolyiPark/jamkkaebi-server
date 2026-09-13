package com.example.jamkkaebi.friend.dto.response;

import com.example.jamkkaebi.friend.domain.GiftState;

/**
 * 상자 선물 결과.
 *
 * @param jeongseongGained 발신 보상 정성.
 * @param giftState        선물 후 버튼 상태 ({@code SENT})
 */
public record GiftResponse(int jeongseongGained, GiftState giftState) {
}
