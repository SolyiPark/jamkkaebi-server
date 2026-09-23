package com.example.jamkkaebi.artifact.service;

import com.example.jamkkaebi.artifact.domain.Era;
import com.example.jamkkaebi.artifact.domain.UserEraCrystal;
import com.example.jamkkaebi.artifact.repository.UserEraCrystalRepository;
import org.springframework.stereotype.Component;
import org.springframework.transaction.annotation.Transactional;

import java.util.EnumMap;
import java.util.Map;

/**
 * 시대의 결정 지갑. 시대별로 나뉘어 있어 (사용자, 시대) 행 하나가 지갑 하나다.
 *
 * <p>지갑 행은 <b>처음 결정을 적립할 때 만든다.</b>
 */
@Component
public class EraCrystalWallet {

    private final UserEraCrystalRepository repository;

    public EraCrystalWallet(UserEraCrystalRepository repository) {
        this.repository = repository;
    }

    /**
     * 시대의 결정을 적립한다.
     *
     * <p>읽고 더해서 쓰지 않고 UPDATE 한 문장으로 더한다 — 상자 환전과 정화 정산이 같은 지갑에
     * 동시에 들어오면 읽어 둔 옛 잔액이 적립분을 덮어쓴다.
     */
    @Transactional
    public void add(Long userId, Era era, int amount) {
        if (amount <= 0) {
            return;
        }
        if (repository.addCrystal(userId, era, amount) == 0) {
            repository.save(UserEraCrystal.emptyWallet(userId, era));
            repository.addCrystal(userId, era, amount);
        }
    }

    @Transactional(readOnly = true)
    public Map<Era, Integer> balances(Long userId) {
        Map<Era, Integer> balances = new EnumMap<>(Era.class);
        for (Era era : Era.values()) {
            balances.put(era, 0);
        }
        repository.findAllByUserId(userId)
                .forEach(wallet -> balances.put(wallet.getEra(), wallet.getCrystal()));
        return balances;
    }
}
