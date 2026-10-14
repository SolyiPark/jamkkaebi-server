package com.example.jamkkaebi.purification.dto.response;

import com.example.jamkkaebi.relic.domain.Difficulty;
import com.example.jamkkaebi.relic.domain.EntryRoute;
import com.example.jamkkaebi.relic.domain.Phase;
import com.example.jamkkaebi.relic.domain.SealMedium;

import java.math.BigDecimal;
import java.time.LocalDateTime;

/**
 * 발급된 정화 세션.
 *
 * @param sessionToken          결과 보고에 쓰는 1회용 토큰
 * @param expiresAt             보고 마감 시간
 * @param sealMedium            이 유물의 봉인 매개체
 * @param perfectBonusEligible  완벽 정화 보너스 대상인지. 최초 정화에는 적용하지 않는다
 */
public record PurificationStartResponse(
        String sessionToken,
        LocalDateTime expiresAt,
        EntryRoute entryRoute,
        Phase phase,
        Difficulty difficulty,
        SealMedium sealMedium,
        BoardView board,
        BigDecimal rewardMultiplier,
        boolean perfectBonusEligible
) {
}
