package com.example.jamkkaebi.relic.service;

import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import com.example.jamkkaebi.relic.domain.RelicMaster;
import com.example.jamkkaebi.relic.domain.UserRelic;
import com.example.jamkkaebi.relic.repository.UserRelicRepository;
import org.springframework.stereotype.Component;

/**
 * 내 유물을 꺼낸다.
 *
 * <p>유물은 <b>마스터 번호</b>로 가리킨다.
 * <p>없는 번호와 보유하지 않은 유물을 <b>같은 오류로</b> 돌려준다.
 */
@Component
public class RelicOwnership {

    private final UserRelicRepository userRelicRepository;
    private final RelicCatalog catalog;

    public RelicOwnership(UserRelicRepository userRelicRepository, RelicCatalog catalog) {
        this.userRelicRepository = userRelicRepository;
        this.catalog = catalog;
    }

    // 보유한 유물과 그 마스터. 없으면 {@code W001}.
    public Owned require(Long userId, Integer relicId) {
        UserRelic owned = userRelicRepository.findByUserIdAndRelicId(userId, relicId)
                .orElseThrow(() -> new BusinessException(ErrorCode.RELIC_NOT_OWNED));
        return new Owned(owned, catalog.relic(relicId));
    }

    // 이름 짓기·강화 확정
    public Owned requireCompleted(Long userId, Integer relicId) {
        Owned owned = require(userId, relicId);
        if (!owned.userRelic().isCompleted()) {
            throw new BusinessException(ErrorCode.RELIC_NOT_COMPLETED);
        }
        return owned;
    }

    public record Owned(UserRelic userRelic, RelicMaster master) {
    }
}
